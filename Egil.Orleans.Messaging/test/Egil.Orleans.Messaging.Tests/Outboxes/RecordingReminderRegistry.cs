using System.Collections.Concurrent;
using Orleans.Timers;

namespace Egil.Orleans.Messaging.Tests.Outboxes;

public sealed class RecordingReminderRegistry(IReminderRegistry inner) : IReminderRegistry
{
    private readonly ConcurrentQueue<ReminderApiCall> calls = new();
    private readonly ConcurrentDictionary<GrainId, byte> lostResponses = new();
    private readonly ConcurrentDictionary<GrainId, byte> rejectedRemovals = new();
    private readonly ConcurrentDictionary<GrainId, ReminderWriteGate> heldResponses = new();

    public ReminderApiCounts For(GrainId grainId)
    {
        var snapshot = calls.Where(call => call.GrainId == grainId).ToArray();
        return new(
            snapshot.Count(call => call.Operation is nameof(GetReminder) or nameof(GetReminders)),
            snapshot.Count(call => call.Operation == nameof(RegisterOrUpdateReminder)),
            snapshot.Count(call => call.Operation == nameof(UnregisterReminder)));
    }

    public void LoseNextRegistrationResponse(GrainId grainId) => lostResponses.TryAdd(grainId, 0);
    public void RejectRemoval(GrainId grainId) => rejectedRemovals.TryAdd(grainId, 0);

    public ReminderWriteGate HoldRegistrationResponse(GrainId grainId)
    {
        var gate = new ReminderWriteGate();
        if (!heldResponses.TryAdd(grainId, gate))
        {
            throw new InvalidOperationException("A registration response is already held for this grain.");
        }

        return gate;
    }

    public async Task<IGrainReminder> RegisterOrUpdateReminder(
        GrainId callingGrainId, string reminderName, TimeSpan dueTime, TimeSpan period)
    {
        calls.Enqueue(new(callingGrainId, reminderName, nameof(RegisterOrUpdateReminder)));
        var reminder = await inner.RegisterOrUpdateReminder(callingGrainId, reminderName, dueTime, period);
        if (heldResponses.TryGetValue(callingGrainId, out var gate))
        {
            await gate.EnterAsync();
        }

        // The service has persisted and scheduled the reminder, but its caller
        // cannot tell whether the write succeeded when this response is lost.
        if (lostResponses.TryRemove(callingGrainId, out _))
        {
            throw new TimeoutException("The registration response was lost.");
        }

        return reminder;
    }

    public Task UnregisterReminder(GrainId callingGrainId, IGrainReminder reminder)
    {
        calls.Enqueue(new(callingGrainId, reminder.ReminderName, nameof(UnregisterReminder)));
        return rejectedRemovals.ContainsKey(callingGrainId)
            ? Task.FromException(new InvalidOperationException("Reminder removal is unavailable."))
            : inner.UnregisterReminder(callingGrainId, reminder);
    }

    public Task<IGrainReminder?> GetReminder(GrainId callingGrainId, string reminderName)
    {
        calls.Enqueue(new(callingGrainId, reminderName, nameof(GetReminder)));
        return inner.GetReminder(callingGrainId, reminderName);
    }

    public Task<List<IGrainReminder>> GetReminders(GrainId callingGrainId)
    {
        calls.Enqueue(new(callingGrainId, "*", nameof(GetReminders)));
        return inner.GetReminders(callingGrainId);
    }

    private sealed record ReminderApiCall(GrainId GrainId, string ReminderName, string Operation);
}

public sealed record ReminderApiCounts(int Lookups, int Registrations, int Removals);
