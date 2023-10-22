﻿using System.Collections.Generic;
using ProtoBuf;

namespace SentisGameplayImprovements
{
  [ProtoContract]
  public struct GuiGridsResponse
  {
    [ProtoMember(1)]
    public ulong SteamId;
    [ProtoMember(2)]
    public List<string> Grids;
  }
}
