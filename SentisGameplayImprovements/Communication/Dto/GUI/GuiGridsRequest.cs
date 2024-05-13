using ProtoBuf;

namespace SentisGameplayImprovements
{
  [ProtoContract]
  public struct GuiGridsRequest
  {
    [ProtoMember(1)]
    public ulong SteamId;
  }
}
