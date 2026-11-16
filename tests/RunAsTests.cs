namespace PxeVice.Tests
{
    public static class RunAsTests
    {
        public static void Run()
        {
            string user, domain;
            T.Check(RunAs.Split(@"CONTOSO\svc-pxevice", out user, out domain), "DOMAIN\\user");
            T.Equal("svc-pxevice", user, "user from DOMAIN\\user");
            T.Equal("CONTOSO", domain, "domain from DOMAIN\\user");
            T.Check(RunAs.Split("svc-pxevice@contoso.local", out user, out domain), "UPN");
            T.Equal("svc-pxevice@contoso.local", user, "UPN stays the user");
            T.Equal(null, domain, "UPN has no domain parameter");
            T.Check(RunAs.Split("localsvc", out user, out domain), "local account");
            T.Equal(".", domain, "local account domain");
            T.Check(!RunAs.Split(@"CONTOSO\", out user, out domain), "no user name");
            T.Check(!RunAs.Split("", out user, out domain), "empty");

            RunAs.Account = @"CONTOSO\svc";
            T.Check(Cli.UserName.EndsWith(@" as CONTOSO\svc"), "user name says the account: " + Cli.UserName);
            RunAs.Account = null;
            T.Check(!Cli.UserName.Contains(" as "), "own account without the service account");
            using (System.IDisposable none = RunAs.Impersonate()) T.Check(none != null, "no account: impersonation does nothing");

            T.Check(RunAs.IsLocal(System.Environment.MachineName + ".contoso.local"), "this computer by its full name");
            T.Check(!RunAs.IsLocal("CM01.contoso.local"), "another server");
        }
    }
}
