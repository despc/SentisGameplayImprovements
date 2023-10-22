﻿// Decompiled with JetBrains decompiler
// Type: Torch.SharedLibrary.Utils.NotificationUtils
// Assembly: SKO-GridPCULimiter, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null
// MVID: 763A9EB8-F42B-4B0E-ACDE-740B34BF1092
// Assembly location: C:\Users\idesp\Downloads\sko-gridpculimiter-v1.2.1\SKO-GridPCULimiter.dll

using Sandbox.Game;
using VRageMath;

namespace SentisGameplayImprovements
{
    public static class NotificationUtils
    {
        public static void SendTo(long playerId, string message, int timeS, string color = "White") =>
            MyVisualScriptLogicProvider.ShowNotification(message, timeS * 1000, color, playerId);

        public static void SendToAll(string message, int timeS, string color = "White") =>
            MyVisualScriptLogicProvider.ShowNotification(message, timeS * 1000, color);
        
        
        public static void NotifyAllPlayersAround(Vector3D notifyAroundPos, int notifyAroundRadius, string message, bool chatNotification = true, bool onFaceNotification = true, string color = "Red")
        {
            var allPlayersInRadius = PlayerUtils.GetAllPlayersInRadius(notifyAroundPos, notifyAroundRadius);
            foreach (var p in allPlayersInRadius)
            {
                if (chatNotification)
                {
                    ChatUtils.SendTo(p.IdentityId, message);
                }
                if (onFaceNotification)
                {
                    MyVisualScriptLogicProvider.ShowNotification(message, 10000, color,
                        p.IdentityId);
                }
            }
        }
    }
}