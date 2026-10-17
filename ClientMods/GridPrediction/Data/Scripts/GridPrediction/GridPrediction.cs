using System.Collections.Generic;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Game.Components;
using VRage.Game.ModAPI;

// Client-side prediction of the grid one pilots, off by default: the client shows the grid where the server has it
// (no rubber-banding, the turns come a ping late). /grid_prediction turns it on and off. Only on a client: a server
// with ForceDisablePrediction set moves the grids in lockstep. C# 6 and the mod whitelist: the game compiles it.
//
// The game sets every grid's ForceDisablePrediction back to its own (false) each frame, in the grid's physics state
// group (MyEntityPhysicsStateGroup.ClientUpdate, before the session components), and the grid then turns its
// prediction on in its update (MySector, priority 800). So the flag is set every frame, at priority 0: after the
// game's and before the grids'. Off, nothing is set and the game's own prediction is back the next frame.
namespace GridPrediction
{
    [MySessionComponentDescriptor(MyUpdateOrder.BeforeSimulation, 0)]
    public class GridPredictionSwitch : MySessionComponentBase
    {
        private const string Command = "/grid_prediction";

        private readonly List<IMyCubeGrid> _group = new List<IMyCubeGrid>();
        private bool _active;
        private bool _prediction;

        public override void LoadData()
        {
            if (MyAPIGateway.Session == null || MyAPIGateway.Multiplayer == null || MyAPIGateway.Multiplayer.IsServer) return;
            _active = true;
            MyAPIGateway.Utilities.MessageEnteredSender += OnMessage;
        }

        protected override void UnloadData()
        {
            if (!_active) return;
            _active = false;
            MyAPIGateway.Utilities.MessageEnteredSender -= OnMessage;
        }

        public override void UpdateBeforeSimulation()
        {
            if (!_active || _prediction) return;
            var cockpit = MyAPIGateway.Session.Player?.Controller?.ControlledEntity?.Entity as IMyShipController;
            if (cockpit?.CubeGrid == null) return;
            _group.Clear();
            MyAPIGateway.GridGroups.GetGroup(cockpit.CubeGrid, GridLinkTypeEnum.Physical, _group);
            foreach (var g in _group)
            {
                var grid = g as MyCubeGrid;
                if (grid != null) grid.ForceDisablePrediction = true;
            }
        }

        private void OnMessage(ulong sender, string text, ref bool sendToOthers)
        {
            if (text == null || !text.Trim().Equals(Command, System.StringComparison.OrdinalIgnoreCase)) return;
            sendToOthers = false;
            _prediction = !_prediction;
            MyAPIGateway.Utilities.ShowMessage("Предсказание", _prediction
                ? "Клиентское предсказание грида включено: управление без задержки, возможны рывки"
                : "Клиентское предсказание грида выключено: видно точное положение с сервера");
        }
    }
}
