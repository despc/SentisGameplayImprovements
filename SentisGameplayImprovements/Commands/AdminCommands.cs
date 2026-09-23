using System;
using System.Reflection;
using NAPI;
using NLog;
using Sandbox.Game.SessionComponents;
using Sandbox.Game.World;
using Torch.Commands;
using Torch.Commands.Permissions;
using VRage.Game.Definitions.SessionComponents;
using VRage.Game.ModAPI;

namespace SentisGameplayImprovements
{
    [Category("sgi")]
    public class AdminCommands : CommandModule
    {
        public static readonly Logger Log = LogManager.GetCurrentClassLogger();
        
        [Command("gs", ".", null)]
        [Permission(MyPromoteLevel.Moderator)]
        public void GenerateStations()
        {
            MySessionComponentEconomy mySessionComponentEconomy = MySession.Static.GetComponent<MySessionComponentEconomy>();

            Assembly ass = typeof(MySessionComponentEconomy).Assembly;
            Type MyStationGeneratorType = ass.GetType("Sandbox.Game.World.Generator.MyStationGenerator");
            // var CreateAsteroidShapeMethod = MyCompositeShapeProvider.GetMethod
            //     ("CreateAsteroidShape", BindingFlags.Static | BindingFlags.Public | BindingFlags.DeclaredOnly);

            PropertyInfo propertyEconomyDefinition = null;
            foreach (PropertyInfo property in typeof(MySessionComponentEconomy)
                         .GetProperties(BindingFlags.Instance | 
                                        BindingFlags.NonPublic |
                                        BindingFlags.Public))
            {
                if (property.Name.Contains("EconomyDefinition"))
                {
                    propertyEconomyDefinition = property;
                }
            }

            object EconomyDefinition = propertyEconomyDefinition.GetValue(mySessionComponentEconomy);
            object instance = Activator.CreateInstance(MyStationGeneratorType, new object[]{(MySessionComponentEconomyDefinition) EconomyDefinition});
            instance.easyCallMethod("GenerateStations", new object[] {MySession.Static.Factions});
            // new MyStationGenerator().GenerateStations(MySession.Static.Factions);
            // new MyFactionRelationGenerator((MySessionComponentEconomyDefinition) EconomyDefinition).GenerateFactionRelations(MySession.Static.Factions);
            
            mySessionComponentEconomy.easySetField("m_stationStoreItemsFirstGeneration", true);
            mySessionComponentEconomy.easyCallMethod("UpdateStations", new object[0]);
            mySessionComponentEconomy.easySetField("m_stationStoreItemsFirstGeneration", false);

        }
        
        /// <summary>A field of copies of the predefined asteroids with "Field" in their name, around the player.</summary>
        [Command("spawnfield2", ".", null)]
        [Permission(MyPromoteLevel.Moderator)]
        public void SpawnField2(int fieldSize, int count)
        {
            var character = Context.Player?.Character;
            if (character == null)
            {
                Context.Respond("You need a character: the field is made around it.");
                return;
            }
            var error = AsteroidFieldSpawner.CheckArguments(fieldSize, count, 1, 1);
            if (error != null)
            {
                Context.Respond("Not spawned: " + error + ".");
                return;
            }
            var definitions = AsteroidFieldSpawner.FieldDefinitions();
            if (definitions.Count == 0)
            {
                Context.Respond("Not spawned: no predefined asteroid has \"Field\" in its name.");
                return;
            }
            var context = Context;
            Context.Respond("Spawning " + count + " asteroids in a " + fieldSize + " m field...");
            AsteroidFieldSpawner.SpawnPredefined(character.PositionComp.GetPosition(), fieldSize, count, definitions,
                outcome => context.Respond(outcome));
        }

        /// <summary>
        /// A field of generated asteroids around the player: their ores are only the given materials
        /// (and stone), their sizes between radiusMin and radiusMax metres.
        /// </summary>
        [Command("spawnfield", ".", null)]
        [Permission(MyPromoteLevel.Moderator)]
        public void SpawnField(int fieldSize, int count, string materials, int radiusMin = 100, int radiusMax = 150)
        {
            var character = Context.Player?.Character;
            if (character == null)
            {
                Context.Respond("You need a character: the field is made around it.");
                return;
            }
            var error = AsteroidFieldSpawner.CheckArguments(fieldSize, count, radiusMin, radiusMax);
            var ores = AsteroidFieldSpawner.ParseMaterials(materials);
            if (error == null && ores.Length == 0) error = "no material given";
            var unknown = error == null ? AsteroidFieldSpawner.UnknownMaterials(ores) : new string[0];
            if (error == null && unknown.Length > 0) error = "no such voxel material: " + string.Join(", ", unknown);
            if (error != null)
            {
                Context.Respond("Not spawned: " + error + ".");
                return;
            }
            var context = Context;
            Context.Respond("Spawning " + count + " asteroids of " + string.Join(", ", ores) + " in a " + fieldSize + " m field...");
            AsteroidFieldSpawner.SpawnGenerated(character.PositionComp.GetPosition(), fieldSize, count, ores, radiusMin, radiusMax,
                outcome => context.Respond(outcome));
        }
    }
}