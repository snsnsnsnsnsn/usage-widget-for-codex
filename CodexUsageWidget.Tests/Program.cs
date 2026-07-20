using CodexUsageWidget.App.Models;
using CodexUsageWidget.App.Services;
using System.Text.Json;

var failures = new List<string>();

void Check(bool condition, string name)
{
    if (condition) Console.WriteLine($"PASS  {name}");
    else
    {
        Console.WriteLine($"FAIL  {name}");
        failures.Add(name);
    }
}

var sample = """
{"timestamp":"2030-01-01T00:00:00.000Z","payload":{"rate_limits":{"primary":{"used_percent":20.0,"window_minutes":300,"resets_at":1893474000},"secondary":{"used_percent":40.0,"window_minutes":10080,"resets_at":1894060800}}}}
""";
var parsed = LocalSessionUsageProvider.TryParseSessionLine(sample);
Check(parsed is not null, "JSONLレコードを解析できる");
Check(parsed?.FiveHour?.RemainingPercent == 80, "5時間の残り率を計算できる");
Check(parsed?.Weekly?.RemainingPercent == 60, "週間の残り率を計算できる");
Check(parsed?.FiveHour?.WindowMinutes == 300, "5時間枠を期間から識別できる");
Check(parsed?.Weekly?.WindowMinutes == 10080, "週間枠を期間から識別できる");

var clamped = new RateLimitWindow(125, 300, null);
Check(clamped.RemainingPercent == 0, "異常な使用率を安全に丸める");

var weeklyReset = new DateTimeOffset(2030, 1, 8, 0, 0, 0, TimeSpan.Zero);
var paceSample = new RateLimitWindow(24, 10080, weeklyReset);
var projectedPace = paceSample.ProjectedConsumptionPercent(new DateTimeOffset(2030, 1, 3, 0, 0, 0, TimeSpan.Zero));
Check(projectedPace is not null && Math.Abs(projectedPace.Value - 84) < 0.001, "週間の消費ペースを予測できる");
Check(new RateLimitWindow(24, 0, weeklyReset).ProjectedConsumptionPercent(weeklyReset) is null, "期間不明時は消費ペースを表示しない");

var renewalSettings = new WidgetSettings { SubscriptionRenewalAt = new DateTime(2030, 8, 1, 9, 30, 0) };
var restoredRenewalSettings = JsonSerializer.Deserialize<WidgetSettings>(JsonSerializer.Serialize(renewalSettings));
Check(restoredRenewalSettings?.SubscriptionRenewalAt == renewalSettings.SubscriptionRenewalAt, "手入力したサブスク更新日時を保存できる");

var january31 = new DateTime(2030, 1, 31, 9, 30, 0);
var februaryRenewal = SubscriptionRenewalSchedule.AdvanceToFuture(january31, null, new DateTime(2030, 2, 1, 0, 0, 0));
Check(februaryRenewal.NextRenewalAt == new DateTime(2030, 2, 28, 9, 30, 0) && februaryRenewal.AnchorDay == 31,
    "月末の更新日を翌月末へ繰り越せる");
var marchRenewal = SubscriptionRenewalSchedule.AdvanceToFuture(februaryRenewal.NextRenewalAt, februaryRenewal.AnchorDay, new DateTime(2030, 2, 28, 10, 0, 0));
Check(marchRenewal.NextRenewalAt == new DateTime(2030, 3, 31, 9, 30, 0),
    "月末の更新日を翌々月の元の日付へ戻せる");

if (failures.Count > 0)
{
    Console.Error.WriteLine($"{failures.Count} test(s) failed.");
    return 1;
}

Console.WriteLine("All tests passed.");
return 0;
