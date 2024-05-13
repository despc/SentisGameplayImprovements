using System;
using System.Collections.Generic;
using System.IO;
using Sandbox.ModAPI;
using SentisGameplayImprovements.DelayedLogic;
using VRage.Library.Utils;
using SecureMsgHandler = System.Action<ushort, byte[], ulong, bool>;

namespace SentisGameplayImprovements
{
  public static class Communication
  {
    private static SecureMsgHandler messageHandler;
    
    public const ushort NETWORK_ID = 10778;

    public static void RegisterHandlers()
    {
      messageHandler = new SecureMsgHandler(NetworkMessageHandler);
      MyAPIGateway.Multiplayer.RegisterSecureMessageHandler(NETWORK_ID, messageHandler);
      SentisGameplayImprovementsPlugin.Log.Warn("Register communication handlers");
    }

    public static void UnregisterHandlers() => MyAPIGateway.Multiplayer.UnregisterSecureMessageHandler(NETWORK_ID, messageHandler);

    private static void NetworkMessageHandler(ushort id, byte[] bytes, ulong plyID, bool sentFromServer)
    {
      try
      {
        MessageType messageType = (MessageType) bytes[0];
        SentisGameplayImprovementsPlugin.Log.Warn(string.Format("Received message: {0}: {1}", (object) bytes[0], (object) messageType));
        byte[] data = new byte[bytes.Length - 1];
        Array.Copy((Array) bytes, 1, (Array) data, 0, data.Length);
        switch (messageType)
        {
          case MessageType.FixShip:
            FixShip(data, plyID);
            break;
          case MessageType.ListForGuiReq:
            OnListRequestForGui(data, plyID);
            break;
        }
      }
      catch (Exception ex)
      {
        SentisGameplayImprovementsPlugin.Log.Warn(string.Format("Error during message handle! {0}", (object) ex));
      }
    }
    
    public static void OnListRequestForGui(byte[] data, ulong plyId)
    {
      GuiGridsRequest listRequest = MyAPIGateway.Utilities.SerializeFromBinary<GuiGridsRequest>(data);
      var listRequestSteamId = listRequest.SteamId;
      SendGridListToGuiClient(listRequestSteamId);
    }

    private static void SendGridListToGuiClient(ulong SteamId)
    {
      if (SteamId == 0)
      {
        return;
      }
      var playerGaragePath = Path.Combine(SentisGameplayImprovementsPlugin.Config.PathToGarage,
        SteamId.ToString());
      var files = Directory.GetFiles(playerGaragePath, "*.sbc");
      var listFiles = new List<string>(files).FindAll(s => s.EndsWith(".sbc"));
      listFiles.SortNoAlloc((s, s1) => String.Compare(s, s1, StringComparison.Ordinal));
      //var resultListFiles = new List<string>();
      //listFiles.ForEach(s => resultListFiles.Add(s.Replace(".sbc", "")));
      // listFiles
            
            
      var resultListFiles = new List<string>();

      listFiles.SortNoAlloc((s, s1) => string.Compare(s, s1, StringComparison.Ordinal));
      listFiles.ForEach(s => resultListFiles.Add(s.Replace(".sbc", "")));

      var resultListGrids = new List<string>();
      for (var i = 1; i < resultListFiles.Count + 1; i++)
      {
        resultListGrids.Add(i + "." + Path.GetFileName(resultListFiles[i - 1]));
      }
            
      GuiGridsResponse response = new GuiGridsResponse();
      response.Grids = resultListGrids;
      response.SteamId = SteamId;
      SendToClient(MessageType.ListForGuiResp,
        MyAPIGateway.Utilities.SerializeToBinary(response), SteamId);
    }

    private static void FixShip(byte[] data, ulong plyId)
    {
      DelayedProcessor.Instance.AddDelayedAction(DateTime.Now.AddMilliseconds(MyRandom.Instance.Next(300, 2000)), () =>
      {
        FixShipRequest request = MyAPIGateway.Utilities.SerializeFromBinary<FixShipRequest>(data);
        var requestGridId = request.gridId;
        var sender = PlayerUtils.GetPlayer(plyId).IdentityId;
        if (sender == 0)
        {
          return;
        }

        FixShipLogic.DoFixShip(requestGridId, sender);
      });
    }
    
    public static void BroadcastToClients(MessageType type, byte[] data)
    {
      byte[] newData = new byte[data.Length + 1];
      newData[0] = (byte) type;
      data.CopyTo((Array) newData, 1);
      SentisGameplayImprovementsPlugin.Log.Warn(string.Format("Sending message to others: {0}", (object) type));
      MyAPIGateway.Utilities.InvokeOnGameThread((Action) (() => MyAPIGateway.Multiplayer.SendMessageToOthers((ushort) NETWORK_ID, newData)));
    }

    public static void SendToClient(MessageType type, byte[] data, ulong recipient)
    {
      byte[] newData = new byte[data.Length + 1];
      newData[0] = (byte) type;
      data.CopyTo((Array) newData, 1);
      SentisGameplayImprovementsPlugin.Log.Warn(string.Format("Sending message to {0}: {1}", (object) recipient, (object) type));
      MyAPIGateway.Utilities.InvokeOnGameThread((Action) (() => MyAPIGateway.Multiplayer.SendMessageTo((ushort) NETWORK_ID, newData, recipient)));
    }
  }
}
