using ProtoBuf;

namespace SentisGameplayImprovements
{
  [ProtoContract]
  public struct ConvertSyncRequest
  {
    [ProtoMember(1)]
    public long GridEntityId;
    [ProtoMember(2)]
    public bool IsStatic;
  }
}
