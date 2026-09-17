namespace NaverRelayUI
{
    internal static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            if (args.Length > 0 && string.Equals(args[0], "--collect", StringComparison.OrdinalIgnoreCase))
                return HeadlessCollector.RunAsync(args).GetAwaiter().GetResult();

            ApplicationConfiguration.Initialize();
            Application.Run(new MainForm());
            return 0;
        }
    }
}
