namespace RenegadeServer.Logging;

public class Entry
{
    public string Id { get; set; } = "";
    public long Timestamp { get; set; }
    public string Level { get; set; } = "";
    public string Source { get; set; } = "";
    public string Message { get; set; } = "";
}
