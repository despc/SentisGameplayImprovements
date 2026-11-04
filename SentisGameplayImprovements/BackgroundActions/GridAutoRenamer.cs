using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox.Game.Entities;
using Sandbox.Game.Localization;
using Sandbox.Game.Multiplayer;
using Sandbox.Game.World;
using VRage;
using VRage.Game;

namespace SentisGameplayImprovements.BackgroundActions
{
    public class GridAutoRenamer
    {
        private static readonly Random Random = new Random();

        public void CheckAndRename(MyCubeGrid grid)
        {
            if (grid == null || grid.MarkedForClose)
            {
                return;
            }

            var gridDisplayName = grid.DisplayName;
            if (gridDisplayName == null) return;

            if (IsSubgridName(gridDisplayName))
            {
                RenameSubgrids(grid);
                return;
            }

            if (IsDefaultName(gridDisplayName))
            {
                RenameGrid(grid);
            }
        }

        // through the game's request: the name reaches the players too (a plain DisplayName is the server's only)
        private static void SetName(MyCubeGrid grid, string name) => grid.ChangeDisplayNameRequest(name);

        private static bool IsDefaultName(string name) =>
            name.Contains("Small Grid") || name.Contains("Static Grid") || name.Contains("Large Grid")
            || name.Contains("Малая структура") || name.Contains("Большая структура") || name.Contains("Статичная структура");

        // The game names the top grid of a rotor, a hinge or a piston CustomShipName_Subgrid in the language of whoever
        // made it: the server's, or the player's when it came in a blueprint.
        private static readonly HashSet<string> SubgridNames = new HashSet<string>
        {
            "Subgrid", "Подструктура", "Sottogriglia", "Sous-grille", "Subestructura", "Subkonstruktion", "子网格",
        };

        private static bool IsSubgridName(string name)
        {
            if (SubgridNames.Contains(name)) return true;
            try { return name == MyTexts.GetString(MySpaceTexts.CustomShipName_Subgrid); }
            catch { return false; }
        }

        /// <summary>
        /// The grids of the mechanical group still named "Subgrid" become "{main}_Subgrid_{n}": the main grid is the
        /// group's biggest one not named so, n the lowest number its subgrids do not use yet. While the main grid has
        /// the game's default name it is left for the next pass, after that grid got its own.
        /// </summary>
        private static void RenameSubgrids(MyCubeGrid grid)
        {
            var group = MyCubeGridGroups.Static.Mechanical.GetGroup(grid)?.Nodes.Select(n => n.NodeData)
                .Where(g => g != null && !g.MarkedForClose && !g.Closed).ToList();
            if (group == null || group.Count < 2) return;
            var main = group.Where(g => g.DisplayName != null && !IsSubgridName(g.DisplayName))
                .OrderByDescending(g => g.BlocksCount).ThenBy(g => g.EntityId).FirstOrDefault();
            if (main == null || IsDefaultName(main.DisplayName)) return;
            var prefix = main.DisplayName + "_Subgrid_";
            var used = new HashSet<int>();
            foreach (var g in group)
                if (g.DisplayName != null && g.DisplayName.StartsWith(prefix, StringComparison.Ordinal) && int.TryParse(g.DisplayName.Substring(prefix.Length), out var n))
                    used.Add(n);
            var next = 1;
            foreach (var sub in group.Where(g => g != main && g.DisplayName != null && IsSubgridName(g.DisplayName)).OrderBy(g => g.EntityId))
            {
                while (used.Contains(next)) next++;
                SetName(sub, prefix + next);
                used.Add(next);
            }
        }

        private void RenameGrid(MyCubeGrid grid)
        {
            var ownerId = PlayerUtils.GetOwner(grid);
            if (ownerId == 0)
            {
                // a grid ground down to nothing is swept before it closes
                ownerId = grid.GetBlocks().FirstOrDefault()?.BuiltBy ?? 0;
            }

            var ownerName = "Nobody";
            if (ownerId != 0)
            {
                MyIdentity identity = Sync.Players.TryGetIdentity(ownerId);
                if (identity == null)
                {
                    return;
                }
                ownerName = identity.DisplayName;
            }

            var fatBlocks = grid.GetFatBlocks();
            if (fatBlocks.Count == 1)
            {
                var subtypeName = fatBlocks[0].BlockDefinition.Id.SubtypeName;
                if (subtypeName.Contains("Wheel"))
                {
                    SetName(grid, ownerName + "_" + "Wheel");
                    return;
                }

                if (subtypeName.Contains("Piston"))
                {
                    SetName(grid, ownerName + "_" + "Piston");
                    return;
                }

                if (subtypeName.Contains("Rotor"))
                {
                    SetName(grid, ownerName + "_" + "Rotor");
                    return;
                }
            }

            var prefix = "SG";
            if (grid.GridSizeEnum == MyCubeSize.Large)
            {
                prefix = "LG";
            }

            SetName(grid, ownerName + "_" + prefix + "_" + Random.Next(0, 1000));
        }
    }
}