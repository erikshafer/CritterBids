namespace CritterBids.Participants;

public static class ParticipantsConstants
{
    // UUID v5 namespace for deterministic stream ID generation in the Participants BC.
    // Generated once at project initialization — must never be changed or regenerated.
    // Changing this value would invalidate all existing deterministic stream IDs.
    public static readonly Guid ParticipantsNamespace = new Guid("f2f3dcf5-9e37-4f4c-b794-4e7bbeb2373c");
}
