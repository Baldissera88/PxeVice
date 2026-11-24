namespace PxeVice.Tests
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text;
    using System.Text.RegularExpressions;

    public static class FeedbackTests
    {
        public static void Run()
        {
            try
            {
                Encoding();
                IssueLinks();
                MailLinks();
                Scrubbing();
                Log();
                Templates();
                Narrow();
            }
            finally { ProblemLog.Clear(); }
        }

        static void Encoding()
        {
            T.Equal("a%20b%0Ac%26d%3De%2Bf", Feedback.Escape("a b\nc&d=e+f"), "spaces, new lines, & = and + are encoded");
            T.Equal("S%C3%B8lr%C3%B8d", Feedback.Escape("Sølrød"), "non-ASCII is UTF-8, percent-encoded");
            T.Equal("A-z_0.9~", Feedback.Escape("A-z_0.9~"), "unreserved characters stay");
        }

        static Dictionary<string, string> Query(string url)
        {
            Dictionary<string, string> d = new Dictionary<string, string>();
            foreach (string p in url.Substring(url.IndexOf('?') + 1).Split('&'))
            {
                int eq = p.IndexOf('=');
                d[p.Substring(0, eq)] = Uri.UnescapeDataString(p.Substring(eq + 1));
            }
            return d;
        }

        static ProblemReport Sample()
        {
            ProblemReport r = ProblemReport.Make("0.8.0", "Windows 11 Enterprise 24H2", Feedback.ModeName(false, "P01"), new List<string> { "2026-11-24 08:51:59  It failed" }, null);
            r.What = "The reset stops & says nothing.\nSecond line";
            r.Expected = "A reset";
            return r;
        }

        static void IssueLinks()
        {
            ProblemReport r = Sample();
            string url = r.IssueUrl();
            T.Check(url.StartsWith("https://github.com/Baldissera88/PxeVice/issues/new?template=bug_report.yml&title="), "issue link goes to the new issue form of the repo");
            Dictionary<string, string> q = Query(url);
            T.Equal("[Bug]: The reset stops & says nothing.", q["title"], "title is the first line of what happened");
            T.Equal("The reset stops & says nothing.\nSecond line", q["what-happened"], "typed text is kept with its line break");
            T.Equal("0.8.0", q["version"], "version is filled in");
            T.Check(!q.ContainsKey("steps"), "an empty field is left out of the link");
            r.What = new string('x', 20000);
            T.Check(r.IssueUrl().Length <= Feedback.MaxIssueUrl, "a long text is cut so the link fits");
            T.Check(Query(r.IssueUrl())["what-happened"].Contains(Feedback.CutNote), "a cut text says so");
        }

        static void MailLinks()
        {
            string url = Feedback.MailUrl("PxeVice problem", new string('y', 5000), Feedback.MaxMailUrl);
            T.Check(url.StartsWith("mailto:" + Feedback.Email + "?subject="), "mail link goes to the author");
            T.Check(url.Length <= Feedback.MaxMailUrl, "a long mail is cut to fit");
        }

        static void Scrubbing()
        {
            string s = Feedback.Scrub("Server SRVCM1.Contoso.local said 00:15:5D:23:21:3B and 74B241D3-5E0A-4380-B552-F350682ABD84 at https://srvcm1/x for dan@contoso.local, PC-LAB-SPARE", new string[] { "SRVCM1", "SRVCM1.Contoso.local", "PC-LAB-SPARE", "ab" });
            T.Check(!s.Contains("SRVCM1") && !s.Contains("Contoso") && !s.Contains("00:15") && !s.Contains("74B241D3") && !s.Contains("https://") && !s.Contains("dan@") && !s.Contains("LAB-SPARE"), "scrubbed text names nothing: " + s);
            T.Check(s.Contains("<mac>") && s.Contains("<id>") && s.Contains("<link>") && s.Contains("<e-mail>") && s.Contains("<name>"), "replacements say what was taken out");
            T.Equal("ab stays", Feedback.Scrub("ab stays", new string[] { "ab" }), "names shorter than three characters are not scrubbed");
        }

        static void Log()
        {
            ProblemLog.Clear();
            for (int i = 0; i < 15; i++) ProblemLog.Add(new DateTime(2026, 11, 24, 8, 0, i), "error " + i);
            List<string> l = ProblemLog.Recent();
            T.Equal(ProblemLog.Keep, l.Count, "the log keeps the newest ten");
            T.Check(l[0].EndsWith("error 14"), "newest first");
        }

        static void Narrow()
        {
            int[] w = { 100, 80, 60, 50, 40 };
            bool[] none = NarrowFit.Hidden(w, new int[] { 4, 3, 2 }, 70, 400);
            T.Check(!none[0] && !none[4], "everything fits: nothing folds");
            bool[] some = NarrowFit.Hidden(w, new int[] { 4, 3, 2 }, 70, 320);
            T.Check(some[4] && some[3] && !some[2] && !some[0], "the first to fold goes first, and only as many as needed (with More's room)");
            bool[] stay = NarrowFit.Hidden(w, new int[] { 4, 3 }, 70, 100);
            T.Check(stay[4] && stay[3] && !stay[0] && !stay[1] && !stay[2], "items that may not fold stay, even when they do not fit");
            T.Equal(300, NarrowFit.Capped(800, 300), "a dialog is capped to the screen");
            T.Equal(800, NarrowFit.Capped(800, 0), "no room known: unchanged");
            T.Equal(100, NarrowFit.Inside(500, 300, 0, 400), "a window is moved back inside the screen");
            T.Equal(0, NarrowFit.Inside(-50, 300, 0, 400), "and from the left");
        }

        static void Templates()
        {
            string bug = File.ReadAllText(".github/ISSUE_TEMPLATE/bug_report.yml");
            string feat = File.ReadAllText(".github/ISSUE_TEMPLATE/feature_request.yml");
            foreach (FeedbackField f in Sample().Fields()) T.Check(Regex.IsMatch(bug, @"id: " + Regex.Escape(f.Id) + @"\s"), "bug form has the field " + f.Id);
            foreach (FeedbackField f in new FeatureWish().Fields()) T.Check(Regex.IsMatch(feat, @"id: " + Regex.Escape(f.Id) + @"\s"), "feature form has the field " + f.Id);
            T.Check(!bug.Contains("TrackVice") && !feat.Contains("TrackVice"), "the forms say PxeVice");
        }
    }
}
