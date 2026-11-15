namespace PxeVice.Tests
{
    using System;
    using System.Collections.Generic;
    using System.Text;

    public static class PxeTests
    {
        // The demo's PXE boots are written to the log when their time has come, so the demo starts now.
        static readonly DateTime Now = DateTime.UtcNow;

        public static void Run()
        {
            RealLog();
            OfferAndThreads();
            TwoBoots();
            DemoRequests();
            CheckLab();
            CheckRequired();
            CheckKiosk();
            CheckDuplicate();
            CheckUnknownSupportAndBootImage();
            NextBootAfterReset();
            Nal();
            Servers();
            TwoPoints();
        }

        static void Servers()
        {
            List<string> l = LiveSite.SplitServers("DP01, dp01.contoso.local;DP02  \\\\DP03.contoso.local\\");
            T.Equal(3, l.Count, "servers: short and full names count once");
            T.Equal("dp01.contoso.local", l[0], "servers: the full name is kept");
            T.Equal("DP03.contoso.local", l[2], "servers: backslashes removed");
            T.Equal(0, LiveSite.SplitServers(null).Count, "no servers");
        }

        static void TwoPoints()
        {
            DemoSite site = new DemoSite(Now);
            SiteData d = ReadDemo(site);
            PxeRequest kiosk = d.Requests.Find(delegate (PxeRequest r) { return r.MacAddress == "70:B5:E8:21:9C:44"; });
            T.Equal("DP02.contoso.local", kiosk == null ? null : kiosk.Server, "a boot on the second PXE point is read");
            T.Equal(7, d.Requests.Count, "every boot is read once over two PXE points");
        }

        static string L(string text, string time, string thread)
        {
            return "<![LOG[" + text + "]LOG]!><time=\"" + time + "-060\" date=\"11-11-2026\" component=\"SMSPXE\" context=\"\" type=\"1\" thread=\"" + thread + "\" file=\"database.cpp:752\">\r\n";
        }

        static List<LogLine> Parse(string text)
        {
            int used, ln = 0;
            return CmLog.Parse(text, "SMSPXE.log", ref ln, true, null, out used);
        }

        // The lines from SMSPXE.log on the lab site, 11 November 2026.
        static void RealLog()
        {
            StringBuilder b = new StringBuilder();
            string th = "7520 (0x1D60)";
            b.Append(L("Using Management Point: http://SrvCM1.Contoso.local", "08:58:31.100", th));
            b.Append(L("Not in SSL.", "08:58:31.110", th));
            b.Append(L("Request using architecture 9.", "08:58:31.120", th));
            b.Append(L("Not in SSL.", "08:58:31.130", th));
            b.Append(L("Client boot action reply: <ClientIDReply><Identification Unknown=\"0\" DuplicateSMBIOS=\"0\" DuplicateMACAddress=\"0\" ItemKey=\"2046820353\" ServerName=\"\"><Machine><ClientID/><NetbiosName/></Machine></Identification><PXEBootAction LastPXEAdvertisementID=\"\" LastPXEAdvertisementTime=\"\" OfferID=\"\" OfferIDTime=\"\" PkgID=\"\" PackageVersion=\"\" PackagePath=\"\" BootImageID=\"\" Mandatory=\"\"/></ClientIDReply>", "08:58:31.140", th));
            b.Append(L("00:15:5D:23:21:3B, 74B241D3-5E0A-4380-B552-F350682ABD84: no advertisements found", "08:58:31.150", th));
            b.Append(L("Prioritizing local MP http://SrvCM1.Contoso.local.", "08:58:31.160", th));
            b.Append(L("Using Management Point: http://SrvCM1.Contoso.local", "08:58:31.170", th));
            b.Append(L("Not in SSL.", "08:58:31.180", th));
            b.Append(L("00:15:5D:23:21:3B, 74B241D3-5E0A-4380-B552-F350682ABD84: Not serviced.", "08:58:31.190", th));
            List<PxeRequest> rs = PxeLog.Requests("SRVCM1", Parse(b.ToString()));
            T.Equal(1, rs.Count, "real log: one request");
            PxeRequest r = rs[0];
            T.Equal("00:15:5D:23:21:3B", r.MacAddress, "real log: MAC");
            T.Equal("74B241D3-5E0A-4380-B552-F350682ABD84", r.SmbiosGuid, "real log: GUID");
            T.Equal(9, r.Architecture, "real log: architecture");
            T.Equal("x64 UEFI", r.ArchitectureText, "real log: architecture text");
            T.Equal(2046820353L, r.ItemKey, "real log: item key");
            T.Check(r.IsUnknownComputer, "real log: unknown computer");
            T.Equal(RequestOutcome.NotServiced, r.Outcome, "real log: outcome");
            T.Equal("Not serviced: no deployment", r.Result, "real log: result");
            T.Equal(4, r.Lines.Count, "real log: lines of the request");
            T.Equal(new DateTime(2026, 11, 11, 7, 58, 31, 120, DateTimeKind.Utc), r.Utc, "real log: time from the CMTrace bias");
        }

        static void OfferAndThreads()
        {
            StringBuilder b = new StringBuilder();
            b.Append(L("Request using architecture 7.", "09:00:00.000", "1 (0x1)"));
            b.Append(L("Request using architecture 0.", "09:00:00.010", "2 (0x2)"));
            b.Append(L("Client boot action reply: <ClientIDReply><Identification Unknown=\"0\" DuplicateSMBIOS=\"1\" DuplicateMACAddress=\"0\" ItemKey=\"16777350\" ServerName=\"\"><Machine><ClientID>GUID:1</ClientID><NetbiosName>PC-LAB-SPARE</NetbiosName></Machine></Identification><PXEBootAction LastPXEAdvertisementID=\"\" LastPXEAdvertisementTime=\"\" OfferID=\"\" OfferIDTime=\"\" PkgID=\"\" PackageVersion=\"\" PackagePath=\"\" BootImageID=\"\" Mandatory=\"\"/></ClientIDReply>", "09:00:00.020", "2 (0x2)"));
            b.Append(L("Client boot action reply: <ClientIDReply><Identification Unknown=\"1\" DuplicateSMBIOS=\"0\" DuplicateMACAddress=\"0\" ItemKey=\"2046820353\" ServerName=\"\"><Machine><ClientID/><NetbiosName/></Machine></Identification><PXEBootAction LastPXEAdvertisementID=\"\" LastPXEAdvertisementTime=\"\" OfferID=\"P0120006\" OfferIDTime=\"\" PkgID=\"P0100010\" PackageVersion=\"\" PackagePath=\"\" BootImageID=\"P0100005\" Mandatory=\"0\"/></ClientIDReply>", "09:00:00.030", "1 (0x1)"));
            b.Append(L("AA:BB:CC:00:11:22, 11111111-2222-3333-4444-555555555555: found optional advertisement P0120006", "09:00:00.040", "1 (0x1)"));
            b.Append(L("D8:BB:C1:0F:33:72, 03000200-0400-0500-0006-000700080009: no advertisements found", "09:00:00.050", "2 (0x2)"));
            b.Append(L("D8:BB:C1:0F:33:72, 03000200-0400-0500-0006-000700080009: Not serviced.", "09:00:00.060", "2 (0x2)"));
            List<PxeRequest> rs = PxeLog.Requests("DP01", Parse(b.ToString()));
            T.Equal(2, rs.Count, "two machines at once: two requests");
            PxeRequest served = rs.Find(delegate (PxeRequest x) { return x.MacAddress == "AA:BB:CC:00:11:22"; });
            PxeRequest dup = rs.Find(delegate (PxeRequest x) { return x.MacAddress == "D8:BB:C1:0F:33:72"; });
            T.Equal(RequestOutcome.Offered, served.Outcome, "offer on its own thread");
            T.Equal("P0120006", served.OfferId, "offer ID");
            T.Equal("P0100005", served.BootImageId, "boot image");
            T.Equal(false, served.Mandatory, "optional");
            T.Equal(7, served.Architecture, "architecture of its own thread");
            T.Equal(0, dup.Architecture, "architecture of the other thread");
            T.Check(dup.DuplicateSmbios, "duplicate SMBIOS");
            T.Equal("PC-LAB-SPARE", dup.Netbios, "NetBIOS name from the reply");
            T.Equal(16777350L, dup.ItemKey, "known device");
            T.Check(!dup.IsUnknownComputer, "known");
        }

        static void TwoBoots()
        {
            StringBuilder b = new StringBuilder();
            string th = "9 (0x9)";
            b.Append(L("Request using architecture 9.", "10:00:00.000", th));
            b.Append(L("00:15:5D:23:21:3B, 74B241D3-5E0A-4380-B552-F350682ABD84: no advertisements found", "10:00:00.100", th));
            b.Append(L("00:15:5D:23:21:3B, 74B241D3-5E0A-4380-B552-F350682ABD84: Not serviced.", "10:00:00.200", th));
            b.Append(L("Request using architecture 9.", "10:00:30.000", th));
            b.Append(L("00:15:5D:23:21:3B, 74B241D3-5E0A-4380-B552-F350682ABD84: found optional advertisement P0120006", "10:00:30.100", th));
            List<PxeRequest> rs = PxeLog.Requests("DP01", Parse(b.ToString()));
            T.Equal(2, rs.Count, "two boots of one machine");
            T.Equal(RequestOutcome.Offered, rs[0].Outcome, "newest first: offered");
            T.Equal(RequestOutcome.NotServiced, rs[1].Outcome, "then not serviced");
        }

        static SiteData ReadDemo(DemoSite site)
        {
            PxeLogStore store = new PxeLogStore();
            return Engine.Read(site, store);
        }

        static Machine Find(SiteData d, string mac)
        {
            foreach (Machine m in d.Machines) if (m.AllMacs.Contains(mac)) return m;
            return null;
        }

        static void DemoRequests()
        {
            DemoSite site = new DemoSite(Now);
            SiteData d = ReadDemo(site);
            T.Equal(7, d.Requests.Count, "demo: seven PXE requests");
            Machine lab = Find(d, "00:15:5D:23:21:3B");
            T.Equal(2, lab.Requests.Count, "lab machine: two boots");
            T.Check(lab.NotServiced, "lab machine: last boot not serviced");
            T.Equal("Not serviced + PXE flag + Unknown record", lab.Found, "lab machine: found");
            Machine spare = Find(d, "D8:BB:C1:0F:33:72");
            T.Check(spare != null, "a machine known only from its request is listed");
            T.Equal("PC-LAB-SPARE", spare.Name, "request machine gets the device record it booted as");
            T.Equal(null, Find(d, "9C:7B:EF:44:10:5A"), "a machine that was served is not listed");
            T.Check(Find(d, "70:B5:E8:21:9C:44") != null, "kiosk listed");
        }

        static List<Finding> Check(DemoSite site, string mac)
        {
            SiteData d = ReadDemo(site);
            return Checker.Check(site, Find(d, mac), new CheckCache(), Now);
        }

        static bool Has(List<Finding> f, Severity s, string title)
        {
            return f.Exists(delegate (Finding x) { return x.Severity == s && x.Title.StartsWith(title); });
        }

        static string Titles(List<Finding> f)
        {
            List<string> t = new List<string>();
            foreach (Finding x in f) t.Add(x.Severity + ":" + x.Title);
            return string.Join(" | ", t.ToArray());
        }

        static void CheckLab()
        {
            List<Finding> f = Check(new DemoSite(Now), "00:15:5D:23:21:3B");
            string t = Titles(f);
            T.Check(Has(f, Severity.Blocks, "Leftover Unknown device record"), "lab: Unknown record blocks. " + t);
            T.Check(Has(f, Severity.Blocks, "Last PXE boot was not serviced"), "lab: last boot. " + t);
            T.Check(Has(f, Severity.Blocks, "No task sequence is deployed to it"), "lab: the Unknown record is in no collection. " + t);
            T.Check(Has(f, Severity.Info, "PXE flag for an available deployment"), "lab: flag of an available deployment does not block. " + t);
            T.Check(Checker.Blocked(f), "lab: blocked");
            T.Equal(Severity.Blocks, f[0].Severity, "worst first");
            string text = Checker.AsText(new DemoSiteMachine().Get(), f, Now);
            T.Check(text.Contains("[Blocks PXE] Leftover Unknown device record") && text.Contains("To do: "), "ticket text");
        }

        sealed class DemoSiteMachine
        {
            public Machine Get()
            {
                return Find(ReadDemo(new DemoSite(Now)), "00:15:5D:23:21:3B");
            }
        }

        static void CheckRequired()
        {
            List<Finding> f = Check(new DemoSite(Now), "F8:75:A4:0B:7E:21");
            string t = Titles(f);
            T.Check(Has(f, Severity.Blocks, "Required deployment already offered"), "HR: required flag blocks. " + t);
            T.Check(!Has(f, Severity.Ok, "Can get"), "HR: nothing it can get while flagged. " + t);
            T.Check(!Has(f, Severity.Blocks, "No task sequence"), "HR: deployment found. " + t);
        }

        static void CheckKiosk()
        {
            List<Finding> f = Check(new DemoSite(Now), "70:B5:E8:21:9C:44");
            string t = Titles(f);
            T.Check(Has(f, Severity.Blocks, "Deployment not available to PXE"), "kiosk: clients only. " + t);
            Finding x = f.Find(delegate (Finding y) { return y.Title == "Deployment not available to PXE"; });
            T.Check(x != null && x.What.Contains("Only Configuration Manager clients"), "kiosk: says what it is set to");
        }

        static void CheckDuplicate()
        {
            List<Finding> f = Check(new DemoSite(Now), "D8:BB:C1:0F:33:72");
            string t = Titles(f);
            T.Check(Has(f, Severity.Warning, "Duplicate SMBIOS GUID"), "spare: duplicate. " + t);
            T.Check(Has(f, Severity.Blocks, "No task sequence is deployed to it"), "spare: the record it matched has no deployment. " + t);
        }

        static void CheckUnknownSupportAndBootImage()
        {
            DemoSite site = new DemoSite(Now);
            site.Points[0].SupportsUnknown = false;
            site.Content["P0100005"] = new List<string>(new string[] { "DP02.contoso.local" });
            List<Finding> f = Check(site, "3C:52:82:4A:91:07");
            string t = Titles(f);
            T.Check(Has(f, Severity.Blocks, "Unknown computer support is off"), "unknown support. " + t);
            T.Check(Has(f, Severity.Blocks, "Boot image not on the PXE distribution point"), "boot image. " + t);
            T.Check(Has(f, Severity.Info, "Deployment expired"), "expired deployment is a note when another one works. " + t);

            DemoSite ok = new DemoSite(Now);
            List<Finding> g = Check(ok, "3C:52:82:4A:91:07");
            T.Check(Has(g, Severity.Ok, "Can get P0120006"), "unknown: can get the available deployment. " + Titles(g));
            T.Check(Has(g, Severity.Ok, "Last PXE boot was offered"), "unknown: last boot offered. " + Titles(g));
        }

        static void NextBootAfterReset()
        {
            DemoSite site = new DemoSite(Now);
            site.NextBootSeconds = 1;
            PxeLogStore store = new PxeLogStore();
            SiteData d = Engine.Read(site, store);
            Machine lab = Find(d, "00:15:5D:23:21:3B");
            Plan p = Engine.BuildPlan(site, new List<Machine>(new Machine[] { lab }), new ResetOptions());
            Engine.Execute(site, p, null);
            System.Threading.Thread.Sleep(1300);
            SiteData after = Engine.Read(site, store);
            PxeRequest r = null;
            foreach (PxeRequest x in after.Requests) if (x.MacAddress == "00:15:5D:23:21:3B") { r = x; break; }
            T.Check(r != null && r.Served, "after the reset the machine is offered a task sequence");
            T.Equal("P0120006", r == null ? null : r.OfferId, "offered the deployment for unknown computers");
            T.Equal(null, Find(after, "00:15:5D:23:21:3B"), "the served machine leaves the list");
        }

        static void Nal()
        {
            T.Equal("DP01.contoso.local", LiveSite.NalServer("[\"Display=\\\\DP01.contoso.local\\\"]MSWNET:[\"SMS_SITE=P01\"]\\\\DP01.contoso.local\\"), "server from a NAL path");
            T.Check(Checker.SameServer("DP01", "dp01.contoso.local"), "short and full names match");
        }
    }
}
