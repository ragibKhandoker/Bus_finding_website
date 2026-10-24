namespace SmartCommute.Server.ViewModels;

public class StatusViewModel
{
    public string Environment { get; set; } = "";
    public bool DatabaseConnected { get; set; }
    public int Users { get; set; }
    public int Buses { get; set; }
    public int Stops { get; set; }
    public int ActiveRides { get; set; }
}
