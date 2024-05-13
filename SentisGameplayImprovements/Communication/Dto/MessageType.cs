using ProtoBuf;

namespace SentisGameplayImprovements
{
  [ProtoContract]
  public enum MessageType : byte
  {
    FixShip,
    ListForGuiReq,
    ListForGuiResp
  }
}
