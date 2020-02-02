namespace NAPS2.Remoting.Network
{
    public class NetworkScanServerOptions
    {
        public int Port { get; set; }
        
        public bool AllowDiscovery { get; set; } = true;

        public int DiscoveryPort { get; set; }
    }
}