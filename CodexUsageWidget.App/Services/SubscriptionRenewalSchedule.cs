namespace CodexUsageWidget.App.Services;

public static class SubscriptionRenewalSchedule
{
    public static (DateTime NextRenewalAt, int AnchorDay) AdvanceToFuture(
        DateTime renewalAt,
        int? preferredDay,
        DateTime now)
    {
        var anchorDay = preferredDay is >= 1 and <= 31 ? preferredDay.Value : renewalAt.Day;
        var nextRenewalAt = renewalAt;

        while (nextRenewalAt <= now)
        {
            var nextMonth = new DateTime(nextRenewalAt.Year, nextRenewalAt.Month, 1).AddMonths(1);
            var day = Math.Min(anchorDay, DateTime.DaysInMonth(nextMonth.Year, nextMonth.Month));
            nextRenewalAt = new DateTime(nextMonth.Year, nextMonth.Month, day).Add(nextRenewalAt.TimeOfDay);
        }

        return (nextRenewalAt, anchorDay);
    }
}
