#nullable enable

using System;

namespace VividSoul.Runtime.AI
{
    public sealed class ReminderScheduler
    {
        private const float DefaultScanIntervalSeconds = 2f;
        private readonly ReminderStore reminderStore;
        private readonly Action<ReminderRecord> onReminderDelivered;
        private float nextScanAtUnscaledTime;
        private bool isActive;

        public ReminderScheduler(ReminderStore reminderStore, Action<ReminderRecord> onReminderDelivered)
        {
            this.reminderStore = reminderStore ?? throw new ArgumentNullException(nameof(reminderStore));
            this.onReminderDelivered = onReminderDelivered ?? throw new ArgumentNullException(nameof(onReminderDelivered));
        }

        public void Activate()
        {
            isActive = true;
            nextScanAtUnscaledTime = 0f;
        }

        public void Deactivate()
        {
            isActive = false;
        }

        public void NotifyApplicationFocus(bool hasFocus)
        {
            if (hasFocus)
            {
                nextScanAtUnscaledTime = 0f;
            }
        }

        public void Tick(float unscaledTime)
        {
            if (!isActive || unscaledTime < nextScanAtUnscaledTime)
            {
                return;
            }

            nextScanAtUnscaledTime = unscaledTime + DefaultScanIntervalSeconds;
            ScanNow();
        }

        public void RequestImmediateScan()
        {
            nextScanAtUnscaledTime = 0f;
        }

        private void ScanNow()
        {
            var nowUtc = DateTimeOffset.UtcNow;
            var dueReminders = reminderStore.LoadDuePending(nowUtc);
            foreach (var reminder in dueReminders)
            {
                var firingReminder = reminderStore.MarkFiring(reminder.Id, nowUtc);
                var deliveredReminder = reminderStore.MarkDelivered(firingReminder.Id, nowUtc);
                onReminderDelivered(deliveredReminder);
            }
        }
    }
}
