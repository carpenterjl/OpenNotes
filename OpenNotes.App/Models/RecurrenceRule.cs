namespace OpenNotes.Models;

public enum RecurrenceFrequency { Daily, Weekly, Monthly, Yearly, Custom }

public class RecurrenceRule
{
    public RecurrenceFrequency Frequency { get; set; }
    public int Interval { get; set; } = 1;
    public List<DayOfWeek> DaysOfWeek { get; set; } = [];
    public int? DayOfMonth { get; set; }
    public int? MonthOfYear { get; set; }
    public DateTime? EndDate { get; set; }
    public int? MaxOccurrences { get; set; }
}
