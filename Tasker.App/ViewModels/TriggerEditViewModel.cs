using System.Xml;
using CommunityToolkit.Mvvm.ComponentModel;
using Tasker.Core;

namespace Tasker_App.ViewModels;

/// <summary>Editable view-model for a single trigger. Maps to/from <see cref="TriggerDto"/>.</summary>
public partial class TriggerEditViewModel : ObservableObject
{
    public static IReadOnlyList<TriggerKind> Kinds { get; } = new[]
    {
        TriggerKind.OneTime, TriggerKind.Daily, TriggerKind.Weekly, TriggerKind.Monthly,
        TriggerKind.MonthlyDOW, TriggerKind.AtStartup, TriggerKind.AtLogOn, TriggerKind.OnIdle,
        TriggerKind.OnEvent, TriggerKind.OnSessionStateChange,
    };

    /// <summary>Friendly, plain-language labels for each trigger kind (shown in the Type dropdown).</summary>
    public static IReadOnlyList<TriggerKindOption> KindOptions { get; } = new[]
    {
        new TriggerKindOption(TriggerKind.OneTime, "One time"),
        new TriggerKindOption(TriggerKind.Daily, "On a daily schedule"),
        new TriggerKindOption(TriggerKind.Weekly, "On a weekly schedule"),
        new TriggerKindOption(TriggerKind.Monthly, "On a monthly schedule (by date)"),
        new TriggerKindOption(TriggerKind.MonthlyDOW, "On a monthly schedule (by weekday)"),
        new TriggerKindOption(TriggerKind.AtStartup, "When the computer starts"),
        new TriggerKindOption(TriggerKind.AtLogOn, "When a user signs in"),
        new TriggerKindOption(TriggerKind.OnIdle, "When the computer is idle"),
        new TriggerKindOption(TriggerKind.OnEvent, "When a specific event is logged"),
        new TriggerKindOption(TriggerKind.OnSessionStateChange, "On lock, unlock, or remote connect"),
    };

    public static IReadOnlyList<string> SessionStates { get; } = new[]
    {
        "ConsoleConnect", "ConsoleDisconnect", "RemoteConnect", "RemoteDisconnect", "SessionLock", "SessionUnlock",
    };

    [ObservableProperty] public partial TriggerKind Kind { get; set; } = TriggerKind.OneTime;
    [ObservableProperty] public partial bool Enabled { get; set; } = true;
    [ObservableProperty] public partial DateTimeOffset StartDate { get; set; } = DateTimeOffset.Now;
    [ObservableProperty] public partial TimeSpan StartTime { get; set; } = DateTime.Now.TimeOfDay;
    [ObservableProperty] public partial int DaysInterval { get; set; } = 1;
    [ObservableProperty] public partial int WeeksInterval { get; set; } = 1;

    [ObservableProperty] public partial bool Monday { get; set; }
    [ObservableProperty] public partial bool Tuesday { get; set; }
    [ObservableProperty] public partial bool Wednesday { get; set; }
    [ObservableProperty] public partial bool Thursday { get; set; }
    [ObservableProperty] public partial bool Friday { get; set; }
    [ObservableProperty] public partial bool Saturday { get; set; }
    [ObservableProperty] public partial bool Sunday { get; set; }

    [ObservableProperty] public partial string DaysOfMonthText { get; set; } = "1";
    [ObservableProperty] public partial bool RunOnLastDayOfMonth { get; set; }

    [ObservableProperty] public partial bool WeekFirst { get; set; } = true;
    [ObservableProperty] public partial bool WeekSecond { get; set; }
    [ObservableProperty] public partial bool WeekThird { get; set; }
    [ObservableProperty] public partial bool WeekFourth { get; set; }
    [ObservableProperty] public partial bool WeekLast { get; set; }

    [ObservableProperty] public partial bool Jan { get; set; } = true;
    [ObservableProperty] public partial bool Feb { get; set; } = true;
    [ObservableProperty] public partial bool Mar { get; set; } = true;
    [ObservableProperty] public partial bool Apr { get; set; } = true;
    [ObservableProperty] public partial bool MayMonth { get; set; } = true;
    [ObservableProperty] public partial bool Jun { get; set; } = true;
    [ObservableProperty] public partial bool Jul { get; set; } = true;
    [ObservableProperty] public partial bool Aug { get; set; } = true;
    [ObservableProperty] public partial bool Sep { get; set; } = true;
    [ObservableProperty] public partial bool Oct { get; set; } = true;
    [ObservableProperty] public partial bool Nov { get; set; } = true;
    [ObservableProperty] public partial bool Dec { get; set; } = true;

    [ObservableProperty] public partial int DelayMinutes { get; set; }
    [ObservableProperty] public partial string LogonUser { get; set; } = string.Empty;
    [ObservableProperty] public partial string Subscription { get; set; } = string.Empty;
    [ObservableProperty] public partial string StateChange { get; set; } = "ConsoleConnect";

    [ObservableProperty] public partial bool RepeatEnabled { get; set; }
    [ObservableProperty] public partial int RepeatEveryMinutes { get; set; } = 60;
    [ObservableProperty] public partial int RepeatForMinutes { get; set; }

    [ObservableProperty] public partial bool ExpiresEnabled { get; set; }
    [ObservableProperty] public partial DateTimeOffset ExpireDate { get; set; } = DateTimeOffset.Now.AddYears(1);
    [ObservableProperty] public partial TimeSpan ExpireTime { get; set; } = DateTime.Now.TimeOfDay;

    public bool ShowStart => Kind is TriggerKind.OneTime or TriggerKind.Daily or TriggerKind.Weekly
        or TriggerKind.Monthly or TriggerKind.MonthlyDOW;
    public bool ShowDaily => Kind == TriggerKind.Daily;
    public bool ShowWeekly => Kind == TriggerKind.Weekly;
    public bool ShowMonthly => Kind == TriggerKind.Monthly;
    public bool ShowMonthlyDow => Kind == TriggerKind.MonthlyDOW;
    public bool ShowWeekdays => Kind is TriggerKind.Weekly or TriggerKind.MonthlyDOW;
    public bool ShowMonths => Kind is TriggerKind.Monthly or TriggerKind.MonthlyDOW;
    public bool ShowDelay => Kind is TriggerKind.AtStartup or TriggerKind.AtLogOn or TriggerKind.OnSessionStateChange;
    public bool ShowLogonUser => Kind == TriggerKind.AtLogOn;
    public bool ShowEvent => Kind == TriggerKind.OnEvent;
    public bool ShowSession => Kind == TriggerKind.OnSessionStateChange;
    public bool ShowRepeatExpiry => ShowStart;

    public string Header => Kind switch
    {
        TriggerKind.OneTime => "One time",
        TriggerKind.Daily => "Daily",
        TriggerKind.Weekly => "Weekly",
        TriggerKind.Monthly => "Monthly (days)",
        TriggerKind.MonthlyDOW => "Monthly (day of week)",
        TriggerKind.AtStartup => "At system startup",
        TriggerKind.AtLogOn => "At log on",
        TriggerKind.OnIdle => "On idle",
        TriggerKind.OnEvent => "On an event",
        TriggerKind.OnSessionStateChange => "On session state change",
        _ => Kind.ToString(),
    };

    /// <summary>
    /// The currently selected friendly option, kept in sync with <see cref="Kind"/>. Bound to the
    /// Type ComboBox's SelectedItem so it shows the plain-language label while editing the enum.
    /// </summary>
    public TriggerKindOption SelectedKindOption
    {
        get => KindOptions.FirstOrDefault(o => o.Value == Kind) ?? KindOptions[0];
        set { if (value is not null && value.Value != Kind) Kind = value.Value; }
    }

    partial void OnKindChanged(TriggerKind value)
    {
        foreach (var p in new[]
        {
            nameof(ShowStart), nameof(ShowDaily), nameof(ShowWeekly), nameof(ShowMonthly),
            nameof(ShowMonthlyDow), nameof(ShowWeekdays), nameof(ShowMonths), nameof(ShowDelay),
            nameof(ShowLogonUser), nameof(ShowEvent), nameof(ShowSession), nameof(ShowRepeatExpiry),
            nameof(Header), nameof(SelectedKindOption),
        })
            OnPropertyChanged(p);
    }

    public TriggerEditViewModel() { }

    public static TriggerEditViewModel FromDto(TriggerDto dto)
    {
        var vm = new TriggerEditViewModel
        {
            Kind = Kinds.Contains(dto.Kind) ? dto.Kind : TriggerKind.OneTime,
            Enabled = dto.Enabled,
            DaysInterval = Math.Max(1, dto.DaysInterval),
            WeeksInterval = Math.Max(1, dto.WeeksInterval),
            LogonUser = dto.UserId ?? string.Empty,
            Subscription = dto.Subscription ?? string.Empty,
            StateChange = dto.StateChange ?? "ConsoleConnect",
            RunOnLastDayOfMonth = dto.RunOnLastDayOfMonth,
        };
        if (dto.StartBoundary is { } sb) { vm.StartDate = new DateTimeOffset(sb); vm.StartTime = sb.TimeOfDay; }
        foreach (var d in dto.DaysOfWeek) vm.SetDay(d, true);
        if (dto.DaysOfMonth.Count > 0) vm.DaysOfMonthText = string.Join(",", dto.DaysOfMonth);

        if (dto.MonthsOfYear.Count > 0) vm.SetMonths(dto.MonthsOfYear);
        vm.WeekFirst = dto.WeeksOfMonth.Contains("FirstWeek");
        vm.WeekSecond = dto.WeeksOfMonth.Contains("SecondWeek");
        vm.WeekThird = dto.WeeksOfMonth.Contains("ThirdWeek");
        vm.WeekFourth = dto.WeeksOfMonth.Contains("FourthWeek");
        vm.WeekLast = dto.WeeksOfMonth.Contains("LastWeek") || dto.RunOnLastWeek;
        if (dto.WeeksOfMonth.Count == 0 && !dto.RunOnLastWeek) vm.WeekFirst = true;

        if (!string.IsNullOrEmpty(dto.Delay))
        {
            try { vm.DelayMinutes = (int)XmlConvert.ToTimeSpan(dto.Delay).TotalMinutes; } catch { }
        }
        if (!string.IsNullOrEmpty(dto.RepetitionInterval))
        {
            vm.RepeatEnabled = true;
            try { vm.RepeatEveryMinutes = (int)XmlConvert.ToTimeSpan(dto.RepetitionInterval).TotalMinutes; } catch { }
            try { if (!string.IsNullOrEmpty(dto.RepetitionDuration)) vm.RepeatForMinutes = (int)XmlConvert.ToTimeSpan(dto.RepetitionDuration).TotalMinutes; } catch { }
        }
        if (dto.EndBoundary is { } eb) { vm.ExpiresEnabled = true; vm.ExpireDate = new DateTimeOffset(eb); vm.ExpireTime = eb.TimeOfDay; }
        return vm;
    }

    public TriggerDto ToDto()
    {
        var start = StartDate.Date + StartTime;
        var dto = new TriggerDto
        {
            Kind = Kind,
            Enabled = Enabled,
            StartBoundary = start,
            DaysInterval = Math.Max(1, DaysInterval),
            WeeksInterval = Math.Max(1, WeeksInterval),
            Subscription = Subscription,
            StateChange = StateChange,
            RunOnLastDayOfMonth = RunOnLastDayOfMonth,
        };

        if (ShowWeekdays)
        {
            if (Monday) dto.DaysOfWeek.Add("Monday");
            if (Tuesday) dto.DaysOfWeek.Add("Tuesday");
            if (Wednesday) dto.DaysOfWeek.Add("Wednesday");
            if (Thursday) dto.DaysOfWeek.Add("Thursday");
            if (Friday) dto.DaysOfWeek.Add("Friday");
            if (Saturday) dto.DaysOfWeek.Add("Saturday");
            if (Sunday) dto.DaysOfWeek.Add("Sunday");
        }
        if (Kind == TriggerKind.Monthly)
        {
            foreach (var part in DaysOfMonthText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                if (int.TryParse(part, out var d) && d is >= 1 and <= 31) dto.DaysOfMonth.Add(d);
        }
        if (ShowMonthlyDow)
        {
            if (WeekFirst) dto.WeeksOfMonth.Add("FirstWeek");
            if (WeekSecond) dto.WeeksOfMonth.Add("SecondWeek");
            if (WeekThird) dto.WeeksOfMonth.Add("ThirdWeek");
            if (WeekFourth) dto.WeeksOfMonth.Add("FourthWeek");
            if (WeekLast) { dto.WeeksOfMonth.Add("LastWeek"); dto.RunOnLastWeek = true; }
        }
        if (ShowMonths) dto.MonthsOfYear.AddRange(CheckedMonths());

        if (ShowDelay && DelayMinutes > 0)
            dto.Delay = XmlConvert.ToString(TimeSpan.FromMinutes(DelayMinutes));
        if (Kind == TriggerKind.AtLogOn && !string.IsNullOrWhiteSpace(LogonUser))
            dto.UserId = LogonUser.Trim();

        if (RepeatEnabled && RepeatEveryMinutes > 0)
        {
            dto.RepetitionInterval = XmlConvert.ToString(TimeSpan.FromMinutes(RepeatEveryMinutes));
            if (RepeatForMinutes > 0)
                dto.RepetitionDuration = XmlConvert.ToString(TimeSpan.FromMinutes(RepeatForMinutes));
        }
        if (ExpiresEnabled)
            dto.EndBoundary = ExpireDate.Date + ExpireTime;
        return dto;
    }

    private IEnumerable<string> CheckedMonths()
    {
        if (Jan) yield return "January";
        if (Feb) yield return "February";
        if (Mar) yield return "March";
        if (Apr) yield return "April";
        if (MayMonth) yield return "May";
        if (Jun) yield return "June";
        if (Jul) yield return "July";
        if (Aug) yield return "August";
        if (Sep) yield return "September";
        if (Oct) yield return "October";
        if (Nov) yield return "November";
        if (Dec) yield return "December";
    }

    private void SetMonths(IReadOnlyCollection<string> months)
    {
        bool Has(string m) => months.Contains(m);
        Jan = Has("January"); Feb = Has("February"); Mar = Has("March"); Apr = Has("April");
        MayMonth = Has("May"); Jun = Has("June"); Jul = Has("July"); Aug = Has("August");
        Sep = Has("September"); Oct = Has("October"); Nov = Has("November"); Dec = Has("December");
    }

    private void SetDay(string name, bool value)
    {
        switch (name)
        {
            case "Monday": Monday = value; break;
            case "Tuesday": Tuesday = value; break;
            case "Wednesday": Wednesday = value; break;
            case "Thursday": Thursday = value; break;
            case "Friday": Friday = value; break;
            case "Saturday": Saturday = value; break;
            case "Sunday": Sunday = value; break;
        }
    }
}

/// <summary>Pairs a <see cref="TriggerKind"/> with a friendly label for the Type dropdown.</summary>
public sealed class TriggerKindOption
{
    public TriggerKindOption(TriggerKind value, string label)
    {
        Value = value;
        Label = label;
    }

    public TriggerKind Value { get; }
    public string Label { get; }
}
