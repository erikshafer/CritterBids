using CritterBids.Contracts;
using Marten;

namespace CritterBids.Selling;

/// <summary>
/// Wolverine handler for <see cref="SellerRegistrationCompleted"/>.
/// Upserts a <see cref="RegisteredSeller"/> document into the Selling BC's schema
/// when a seller completes registration in the Participants BC.
/// </summary>
/// <remarks>
/// <para>
/// <c>IDocumentSession</c> is injected by Wolverine's <c>SessionVariableSource</c>,
/// which is registered by the primary <c>AddMarten().IntegrateWithWolverine()</c> call
/// in <c>Program.cs</c>. <c>AutoApplyTransactions()</c> commits the session after the
/// handler returns — no explicit <c>SaveChangesAsync()</c> required.
/// </para>
/// <para>
/// Idempotency: <c>session.Store()</c> is a Marten upsert. Processing the same
/// <see cref="SellerRegistrationCompleted"/> message twice produces the same document
/// state — satisfying Wolverine's at-least-once delivery guarantee.
/// </para>
/// </remarks>
public static class SellerRegistrationCompletedHandler
{
    public static void Handle(
        SellerRegistrationCompleted message,
        IDocumentSession session)
    {
        session.Store(new RegisteredSeller { Id = message.ParticipantId });
    }
}
