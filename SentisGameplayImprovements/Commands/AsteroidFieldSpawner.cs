using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using Sandbox.Definitions;
using Sandbox.Engine.Voxels;
using Sandbox.Game.Entities;
using Sandbox.Game.World;
using Sandbox.ModAPI;
using VRage;
using VRage.Game.Entity;
using VRage.Voxels;
using VRageMath;

namespace SentisGameplayImprovements
{
    /// <summary>
    /// The asteroid fields of <c>!sgi spawnfield</c> and <c>!sgi spawnfield2</c>.
    ///
    /// The asteroids are made one after another on a task of their own - generating one takes seconds -
    /// and each is put into the world on the game thread, at a random point of the field where it touches
    /// nothing: no grid, no character, no other asteroid, no planet, with <see cref="Clearance"/> to
    /// spare. A point is looked for <see cref="PlacementTries"/> times; an asteroid that finds none is
    /// left out and counted. Whoever asked is told how many were placed.
    /// </summary>
    public static class AsteroidFieldSpawner
    {
        public static readonly Logger Log = LogManager.GetCurrentClassLogger();

        /// <summary>Metres between an asteroid and anything else.</summary>
        public const double Clearance = 20;

        /// <summary>Random points tried for one asteroid before it is left out.</summary>
        public const int PlacementTries = 30;

        /// <summary>How long the spawning task waits for the game thread to place one asteroid.</summary>
        private static readonly TimeSpan GameThreadWait = TimeSpan.FromSeconds(30);

        private static readonly string[] StoneMaterials = { "Stone_01", "Stone_02", "Stone_03", "Stone_04", "Stone_05" };

        // ------------------------------------------------------------------------------------ arguments

        /// <summary>Why the arguments cannot make a field, or null when they can.</summary>
        public static string CheckArguments(int fieldSize, int count, int sizeMin, int sizeMax)
        {
            if (count < 1) return "the number of asteroids must be at least 1";
            if (fieldSize < 1) return "the field size must be at least 1 m";
            if (sizeMin < 1 || sizeMax < 1) return "the asteroid size must be at least 1 m";
            if (sizeMin > sizeMax) return "the smallest asteroid size (" + sizeMin + ") is bigger than the largest (" + sizeMax + ")";
            return null;
        }

        /// <summary>The material names of a comma separated list: trimmed, no empty ones, no repeats.</summary>
        public static string[] ParseMaterials(string materials) =>
            (materials ?? "").Split(',').Select(m => m.Trim()).Where(m => m.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        /// <summary>The names in the list that are no voxel material of this world.</summary>
        public static string[] UnknownMaterials(IEnumerable<string> materials) =>
            materials.Where(m => !MyDefinitionManager.Static.TryGetVoxelMaterialDefinition(m, out _)).ToArray();

        // ------------------------------------------------------------------------------------ geometry

        /// <summary>
        /// The edge of the voxel storage the game makes for an asteroid of that size: the size, at least
        /// 64, rounded up to a power of two (MyOctreeStorage does the rounding).
        /// </summary>
        public static int StorageEdge(double size) => MathHelper.GetNearestBiggerPowerOfTwo(Math.Max(64, (int)Math.Ceiling(size)));

        /// <summary>The radius of the sphere around a storage of that edge: nothing of the asteroid is outside it.</summary>
        public static double Extent(int storageEdge) => storageEdge * Math.Sqrt(3) / 2;

        /// <summary>
        /// A random point of the field (a sphere of <paramref name="fieldRadius"/> around
        /// <paramref name="centre"/>) where a sphere of <paramref name="radius"/> is free, as
        /// <paramref name="isFree"/> tells; false after <see cref="PlacementTries"/> points.
        /// </summary>
        public static bool TryPickPosition(Vector3D centre, double fieldRadius, double radius, Random random,
            Func<BoundingSphereD, bool> isFree, out Vector3D position)
        {
            var field = new BoundingSphereD(centre, fieldRadius);
            for (var i = 0; i < PlacementTries; i++)
            {
                position = field.RandomToUniformPointInSphere(random.NextDouble(), random.NextDouble(), random.NextDouble());
                if (isFree(new BoundingSphereD(position, radius))) return true;
            }
            position = default;
            return false;
        }

        /// <summary>
        /// Whether the sphere touches no entity of the world (game thread). A planet counts where its
        /// ground is, not by its bounding box, which holds all of the space around it.
        /// </summary>
        public static bool IsFree(BoundingSphereD sphere) => IsFree(sphere, null);

        /// <summary>The same, not counting the entities of <paramref name="ignore"/> (ids).</summary>
        public static bool IsFree(BoundingSphereD sphere, ICollection<long> ignore)
        {
            var found = new List<MyEntity>();
            MyGamePruningStructure.GetAllTopMostEntitiesInSphere(ref sphere, found);
            foreach (var entity in found)
            {
                if (entity is MyPlanet) continue;
                if (entity.MarkedForClose) continue;
                if (ignore != null && ignore.Contains(entity.EntityId)) continue;
                if (entity.PositionComp.WorldAABB.Intersects(sphere)) return false;
            }
            var planet = MyGamePruningStructure.GetClosestPlanet(sphere.Center);
            if (planet != null && !ClearOfPlanet(sphere, planet.PositionComp.GetPosition(), planet.GetClosestSurfacePointGlobal(sphere.Center)))
                return false;
            return true;
        }

        /// <summary>Whether the sphere is above the ground by its radius (the ground point closest to its centre given).</summary>
        public static bool ClearOfPlanet(BoundingSphereD sphere, Vector3D planetCentre, Vector3D groundPoint)
        {
            var aboveGround = Vector3D.Distance(sphere.Center, planetCentre) - Vector3D.Distance(groundPoint, planetCentre);
            return aboveGround > sphere.Radius;
        }

        // ------------------------------------------------------------------------------------ ids and materials

        /// <summary>The game's id for an asteroid of that storage name (MyProceduralWorldGenerator's).</summary>
        public static long AsteroidEntityId(string storageName) =>
            storageName.GetHashCode64() & 72057594037927935L | 432345564227567616L;

        /// <summary>An asteroid id no entity has: that of the name, else of the name with a number.</summary>
        public static long UniqueEntityId(string storageName, Func<long, bool> exists)
        {
            var id = AsteroidEntityId(storageName);
            for (var n = 1; exists(id); n++) id = AsteroidEntityId(storageName + "#" + n);
            return id;
        }

        /// <summary>
        /// What a voxel's material becomes: stone and the allowed ores stay, any other ore turns into one
        /// of the allowed ones (always the same one for the same ore).
        /// </summary>
        public static byte MapMaterial(byte current, IList<byte> allowed, ICollection<byte> keep)
        {
            if (keep.Contains(current) || allowed.Contains(current)) return current;
            return allowed[current % allowed.Count];
        }

        /// <summary>Turns every ore of the storage other than the allowed ones into an allowed one.</summary>
        public static void ReplaceMaterials(MyStorageBase storage, IList<string> allowedMaterials)
        {
            var allowed = allowedMaterials.Select(m => MyDefinitionManager.Static.GetVoxelMaterialDefinition(m).Index).ToList();
            var keep = new HashSet<byte>(StoneMaterials
                .Select(m => MyDefinitionManager.Static.TryGetVoxelMaterialDefinition(m, out var d) ? d : null)
                .Where(d => d != null).Select(d => d.Index));
            var chunk = Vector3I.Min(new Vector3I(64), storage.Size);
            var cache = new MyStorageData();
            cache.Resize(chunk);
            Vector3I block;
            for (block.Z = 0; block.Z < storage.Size.Z; block.Z += chunk.Z)
            for (block.Y = 0; block.Y < storage.Size.Y; block.Y += chunk.Y)
            for (block.X = 0; block.X < storage.Size.X; block.X += chunk.X)
            {
                var max = block + chunk - 1;
                storage.ReadRange(cache, MyStorageDataTypeFlags.Material, 0, block, max);
                var changed = false;
                Vector3I p;
                for (p.Z = 0; p.Z < chunk.Z; ++p.Z)
                for (p.Y = 0; p.Y < chunk.Y; ++p.Y)
                for (p.X = 0; p.X < chunk.X; ++p.X)
                {
                    var current = cache.Material(ref p);
                    var wanted = MapMaterial(current, allowed, keep);
                    if (wanted == current) continue;
                    cache.Material(ref p, wanted);
                    changed = true;
                }
                if (changed) storage.WriteRange(cache, MyStorageDataTypeFlags.Material, block, max);
            }
        }

        /// <summary>
        /// A storage that holds the voxels themselves, not the generator that made them: what is saved
        /// is what was generated, with its materials replaced.
        /// </summary>
        public static MyOctreeStorage CopyVoxels(MyStorageBase source)
        {
            var target = new MyOctreeStorage(null, source.Size);
            var chunk = Vector3I.Min(new Vector3I(64), source.Size);
            var material = new MyStorageData();
            var content = new MyStorageData();
            material.Resize(chunk);
            content.Resize(chunk);
            Vector3I block;
            for (block.Z = 0; block.Z < source.Size.Z; block.Z += chunk.Z)
            for (block.Y = 0; block.Y < source.Size.Y; block.Y += chunk.Y)
            for (block.X = 0; block.X < source.Size.X; block.X += chunk.X)
            {
                var max = block + chunk - 1;
                source.ReadRange(material, MyStorageDataTypeFlags.Material, 0, block, max);
                source.ReadRange(content, MyStorageDataTypeFlags.Content, 0, block, max);
                target.WriteRange(material, MyStorageDataTypeFlags.Material, block, max);
                target.WriteRange(content, MyStorageDataTypeFlags.Content, block, max);
            }
            return target;
        }

        // ------------------------------------------------------------------------------------ storages

        /// <summary>A generated asteroid of that size, the game's own shape for the seeds.</summary>
        public static MyStorageBase GenerateStorage(int seed, int size, int generatorSeed)
        {
            var providerType = typeof(MyStorageBase).Assembly.GetType("Sandbox.Game.World.Generator.MyCompositeShapeProvider");
            var create = providerType?.GetMethod("CreateAsteroidShape", BindingFlags.Static | BindingFlags.Public | BindingFlags.DeclaredOnly)
                         ?? throw new MissingMethodException("MyCompositeShapeProvider.CreateAsteroidShape");
            var provider = (IMyStorageDataProvider)create.Invoke(null, new object[] { seed, (float)size, generatorSeed, null });
            return new MyOctreeStorage(provider, new Vector3I(StorageEdge(size)));
        }

        /// <summary>A copy of a predefined asteroid (a voxel map storage definition).</summary>
        public static MyStorageBase PredefinedStorage(string storageName)
        {
            var menu = typeof(MyDefinitionManager).Assembly.GetType("Sandbox.Game.Gui.MyGuiScreenDebugSpawnMenu");
            var create = menu?.GetMethod("CreatePredefinedDataStorage", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                         ?? throw new MissingMethodException("MyGuiScreenDebugSpawnMenu.CreatePredefinedDataStorage");
            return (MyStorageBase)create.Invoke(null, new object[] { storageName, null });
        }

        /// <summary>The predefined asteroids made for fields: those with "Field" in their name.</summary>
        public static List<MyVoxelMapStorageDefinition> FieldDefinitions() =>
            MyDefinitionManager.Static.GetVoxelMapStorageDefinitions()
                .Where(definition => definition.Id.SubtypeName.Contains("Field")).ToList();

        // ------------------------------------------------------------------------------------ spawning

        /// <summary>
        /// Generates <paramref name="count"/> asteroids of <paramref name="materials"/> between
        /// <paramref name="sizeMin"/> and <paramref name="sizeMax"/> metres and places them in the field.
        /// Returns the task; <paramref name="report"/> gets the outcome, on the game thread.
        /// </summary>
        public static Task SpawnGenerated(Vector3D centre, int fieldSize, int count, string[] materials, int sizeMin, int sizeMax,
            Action<string> report, List<MyVoxelBase> spawned = null)
        {
            return Task.Run(() => Spawn(count, fieldSize, report, spawned, random =>
            {
                var seed = random.Next(100, 10000000);
                var size = random.Next(sizeMin, sizeMax + 1);
                var generated = GenerateStorage(seed, size, random.Next(0, 9999999));
                ReplaceMaterials(generated, materials);
                return ("FieldAster-" + seed + "r" + size, CopyVoxels(generated));
            }, centre));
        }

        /// <summary>Places <paramref name="count"/> copies of random predefined field asteroids.</summary>
        public static Task SpawnPredefined(Vector3D centre, int fieldSize, int count, List<MyVoxelMapStorageDefinition> definitions,
            Action<string> report, List<MyVoxelBase> spawned = null)
        {
            return Task.Run(() => Spawn(count, fieldSize, report, spawned, random =>
            {
                var name = definitions[random.Next(definitions.Count)].Id.SubtypeName;
                return (name, PredefinedStorage(name));
            }, centre));
        }

        private static void Spawn(int count, int fieldSize, Action<string> report, List<MyVoxelBase> spawned,
            Func<Random, (string Name, MyStorageBase Storage)> make, Vector3D centre)
        {
            var random = new Random();
            int placed = 0, noRoom = 0, failed = 0;
            for (var i = 0; i < count; i++)
            {
                try
                {
                    var (name, storage) = make(random);
                    var voxel = OnGameThread(() => Place(name, storage, centre, fieldSize, random));
                    if (voxel == null)
                    {
                        noRoom++;
                        continue;
                    }
                    placed++;
                    if (spawned != null) lock (spawned) spawned.Add(voxel);
                    Log.Info("Field Spawner: " + (i + 1) + "/" + count + " " + voxel.StorageName + " at " + voxel.PositionComp.GetPosition());
                }
                catch (Exception e)
                {
                    failed++;
                    Log.Error(e, "Field Spawner: asteroid " + (i + 1) + "/" + count + " failed");
                }
            }
            var outcome = "Asteroid field: " + placed + " of " + count + " placed" +
                          (noRoom > 0 ? ", " + noRoom + " found no free place" : "") +
                          (failed > 0 ? ", " + failed + " failed (see the log)" : "");
            Log.Warn("Field Spawner: " + outcome);
            MyAPIGateway.Utilities.InvokeOnGameThread(() => report?.Invoke(outcome));
        }

        /// <summary>Puts the asteroid at a free point of the field; null when there is none (game thread).</summary>
        private static MyVoxelBase Place(string name, MyStorageBase storage, Vector3D centre, double fieldSize, Random random)
        {
            var radius = Extent(storage.Size.AbsMax()) + Clearance;
            if (!TryPickPosition(centre, fieldSize, radius, random, IsFree, out var position)) return null;
            var storageName = UniqueStorageName(name);
            var id = UniqueEntityId(storageName, MyEntityIdentifier.ExistsById);
            var minCorner = position - new Vector3D(storage.Size) * 0.5;
            var voxel = MyWorldGenerator.AddVoxelMap(storageName, storage, minCorner, id);
            if (voxel == null) return null;
            voxel.Name = storageName;
            voxel.AsteroidName = storageName;
            voxel.Save = true;
            return voxel;
        }

        /// <summary>The name, or the name with a number, that no voxel map of the world has (game thread).</summary>
        public static string UniqueStorageName(string name)
        {
            var taken = new HashSet<string>(MySession.Static.VoxelMaps.Instances.Select(v => v.StorageName));
            var candidate = name;
            for (var n = 0; taken.Contains(candidate); n++) candidate = name + "-" + n;
            return candidate;
        }

        /// <summary>Runs the function on the game thread and waits for it.</summary>
        private static T OnGameThread<T>(Func<T> function)
        {
            T result = default;
            Exception error = null;
            // Not disposed: after a timeout the game thread may still set it.
            var done = new ManualResetEventSlim(false);
            MyAPIGateway.Utilities.InvokeOnGameThread(() =>
            {
                try { result = function(); }
                catch (Exception e) { error = e; }
                finally { done.Set(); }
            });
            if (!done.Wait(GameThreadWait)) throw new TimeoutException("the game thread did not place the asteroid in " + GameThreadWait.TotalSeconds + " s");
            if (error != null) throw error;
            return result;
        }
    }
}
