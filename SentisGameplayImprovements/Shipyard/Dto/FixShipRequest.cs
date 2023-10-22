using ProtoBuf;

namespace SentisGameplayImprovements
{
  [ProtoContract]
  public struct FixShipRequest
  {
    [ProtoMember(1)]
    public long gridId;
    
    [ProtoMember(2)]
    public long owner;
  }
}
