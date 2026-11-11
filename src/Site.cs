namespace PxeVice
{
    using System;
    using System.Collections.Generic;
    using System.Data;
    using System.Data.SqlClient;
    using System.Globalization;
    using System.IO;
    using System.Management;
    using System.ServiceProcess;
    using System.Text;

    // Where PxeVice connects. Empty values are found from the SMS Provider.
    public sealed class SiteOptions
    {
        public string SiteServer;
        public string SqlServer;
        public string Database;
        public string PxeServers;

        public static SiteOptions FromPrefs()
        {
            SiteOptions o = new SiteOptions();
            o.SiteServer = Prefs.Get("SiteServer", null);
            o.SqlServer = Prefs.Get("SqlServer", null);
            o.Database = Prefs.Get("Database", null);
            o.PxeServers = Prefs.Get("PxeServers", null);
            return o;
        }
    }

    // What PxeVice found out about the site when it connected.
    public sealed class SiteInfo
    {
        public string SiteServer = "";
        public string Provider = "";
        public string SiteCode = "";
        public string SqlServer = "";
        public string Database = "";
        public readonly List<string> PxeServers = new List<string>();
        public readonly List<string> Notes = new List<string>();
        public bool Demo;

        public string Describe()
        {
            return Describe(" · ");
        }

        // The console gets a plain separator, because its code page may not have the middle dot.
        public string Describe(string sep)
        {
            StringBuilder b = new StringBuilder();
            if (Demo) b.Append("Demo data").Append(sep);
            b.Append(SiteServer).Append(sep).Append("site ").Append(SiteCode);
            b.Append(sep).Append(Database).Append(" on ").Append(SqlServer);
            if (PxeServers.Count > 0) b.Append(sep).Append("PXE: ").Append(string.Join(", ", PxeServers.ToArray()));
            return b.ToString();
        }
    }

    // Everything PxeVice reads from and changes in a site. LiveSite talks to ConfigMgr, DemoSite keeps sample data in memory.
    public interface ISite : IDisposable
    {
        SiteInfo Info { get; }
        List<PxeFlag> ReadFlags();
        List<PxeFlag> FlagsFor(string mac, string guid, uint[] ids);
        Dictionary<string, string> DeploymentNames(ICollection<string> ids);
        List<DeviceRecord> UnknownRecords();
        List<DeviceRecord> FindDevices(string mac, string guid, string name, uint[] ids);
        void ClearFlags(uint[] ids);
        void DeleteDevice(uint id);
        int DeleteFlags(string mac, string guid, uint[] ids);
        List<string> RunningPxeServices(string server);
        void RestartService(string server, string service);
        string PxeLog(string server);
    }

    public static class PxeServices
    {
        // WDS when PXE runs through Windows Deployment Services, SccmPxe for the PXE responder without WDS.
        public static readonly string[] Names = { "WDSServer", "SccmPxe" };
    }

    public sealed class LiveSite : ISite
    {
        readonly SiteInfo info = new SiteInfo();
        ManagementScope scope;

        public SiteInfo Info
        {
            get { return info; }
        }

        public LiveSite(SiteOptions o)
        {
            string server = string.IsNullOrEmpty(o.SiteServer) ? Environment.MachineName : o.SiteServer.Trim();
            info.SiteServer = server;

            ManagementScope root = Scope(server, @"root\SMS");
            root.Connect();
            foreach (ManagementObject p in Query(root, "SELECT Machine, SiteCode FROM SMS_ProviderLocation WHERE ProviderForLocalSite = TRUE"))
            {
                info.Provider = Convert.ToString(p["Machine"]);
                info.SiteCode = Convert.ToString(p["SiteCode"]);
                break;
            }
            if (string.IsNullOrEmpty(info.SiteCode)) throw new InvalidOperationException("No SMS Provider for a local site was found on " + server + ".");
            if (string.IsNullOrEmpty(info.Provider)) info.Provider = server;
            scope = Scope(info.Provider, @"root\SMS\site_" + info.SiteCode);
            scope.Connect();

            info.SqlServer = o.SqlServer;
            info.Database = o.Database;
            if (string.IsNullOrEmpty(info.SqlServer) || string.IsNullOrEmpty(info.Database))
            {
                try
                {
                    foreach (ManagementObject s in Query(scope, "SELECT SQLServerName, SQLDatabaseName FROM SMS_SCI_SiteDefinition WHERE SiteCode = '" + Wql(info.SiteCode) + "'"))
                    {
                        string sql = Convert.ToString(s["SQLServerName"]);
                        string db = Convert.ToString(s["SQLDatabaseName"]);
                        // A named instance is stored with the database, as INSTANCE\CM_P01.
                        int slash = db.LastIndexOf('\\');
                        if (slash > 0)
                        {
                            sql = sql + "\\" + db.Substring(0, slash);
                            db = db.Substring(slash + 1);
                        }
                        if (string.IsNullOrEmpty(info.SqlServer) && sql.Length > 0) info.SqlServer = sql;
                        if (string.IsNullOrEmpty(info.Database) && db.Length > 0) info.Database = db;
                        break;
                    }
                }
                catch (Exception ex) { info.Notes.Add("The site database was not found from the SMS Provider: " + ex.Message); }
            }
            if (string.IsNullOrEmpty(info.SqlServer)) info.SqlServer = server;
            if (string.IsNullOrEmpty(info.Database)) info.Database = "CM_" + info.SiteCode;

            if (!string.IsNullOrEmpty(o.PxeServers))
            {
                foreach (string s in o.PxeServers.Split(new char[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries)) info.PxeServers.Add(s.Trim());
            }
            else
            {
                try
                {
                    foreach (ManagementObject d in Query(scope, "SELECT ServerName FROM SMS_DistributionPointInfo WHERE IsPXE = TRUE"))
                    {
                        string name = Convert.ToString(d["ServerName"]);
                        if (name.Length > 0 && !info.PxeServers.Contains(name)) info.PxeServers.Add(name);
                    }
                }
                catch (Exception ex) { info.Notes.Add("The PXE distribution points were not found: " + ex.Message); }
            }
        }

        static ManagementScope Scope(string server, string ns)
        {
            ConnectionOptions c = new ConnectionOptions();
            c.Impersonation = ImpersonationLevel.Impersonate;
            c.Authentication = AuthenticationLevel.PacketPrivacy;
            c.EnablePrivileges = true;
            c.Timeout = TimeSpan.FromSeconds(30);
            return new ManagementScope(@"\\" + server + @"\" + ns, c);
        }

        static ManagementObjectCollection Query(ManagementScope s, string wql)
        {
            EnumerationOptions e = new EnumerationOptions();
            e.ReturnImmediately = false;
            e.Rewindable = false;
            e.Timeout = TimeSpan.FromSeconds(60);
            return new ManagementObjectSearcher(s, new ObjectQuery(wql), e).Get();
        }

        // WQL strings are escaped with a backslash.
        public static string Wql(string s)
        {
            return (s ?? "").Replace("\\", "\\\\").Replace("'", "\\'");
        }

        // ---------- SQL ----------

        SqlConnection Open()
        {
            SqlConnectionStringBuilder b = new SqlConnectionStringBuilder();
            b.DataSource = info.SqlServer;
            b.InitialCatalog = info.Database;
            b.IntegratedSecurity = true;
            b.ApplicationName = "PxeVice";
            b.ConnectTimeout = 15;
            SqlConnection c = new SqlConnection(b.ConnectionString);
            c.Open();
            return c;
        }

        DataTable Table(string sql, params SqlParameter[] ps)
        {
            using (SqlConnection c = Open())
            using (SqlCommand cmd = new SqlCommand(sql, c))
            {
                cmd.CommandTimeout = 60;
                cmd.Parameters.AddRange(ps);
                DataTable t = new DataTable();
                using (SqlDataReader r = cmd.ExecuteReader()) t.Load(r);
                return t;
            }
        }

        static object Val(DataRow r, string col)
        {
            if (!r.Table.Columns.Contains(col)) return null;
            object v = r[col];
            return v is DBNull ? null : v;
        }

        static string Str(DataRow r, string col)
        {
            object v = Val(r, col);
            return v == null ? null : Convert.ToString(v, CultureInfo.InvariantCulture);
        }

        const string FlagColumns = "l.MachineID, l.SMBIOS_GUID, l.MAC_Addresses, l.Netbios_Name, l.LastPXEAdvertisementID, l.LastPXEAdvertisementTime, l.SiteCode, s.Name0";

        static List<PxeFlag> Flags(DataTable t)
        {
            List<PxeFlag> list = new List<PxeFlag>();
            foreach (DataRow r in t.Rows)
            {
                PxeFlag f = new PxeFlag();
                object id = Val(r, "MachineID");
                f.MachineId = id == null ? 0 : Convert.ToInt64(id, CultureInfo.InvariantCulture);
                f.RawMac = Str(r, "MAC_Addresses");
                f.MacAddress = Ids.Mac(f.RawMac);
                f.SmbiosGuid = Ids.Guid(Str(r, "SMBIOS_GUID"));
                f.Netbios = Str(r, "Netbios_Name");
                if (f.Netbios != null) f.Netbios = f.Netbios.Trim();
                f.DeploymentId = Str(r, "LastPXEAdvertisementID");
                object time = Val(r, "LastPXEAdvertisementTime");
                // The site database keeps times in UTC.
                if (time is DateTime) f.TimeUtc = DateTime.SpecifyKind((DateTime)time, DateTimeKind.Utc);
                f.SiteCode = Str(r, "SiteCode");
                f.DeviceName = Str(r, "Name0");
                list.Add(f);
            }
            return list;
        }

        public List<PxeFlag> ReadFlags()
        {
            return Flags(Table("SELECT " + FlagColumns + " FROM dbo.LastPXEAdvertisement l LEFT JOIN dbo.v_R_System s ON s.ResourceID = l.MachineID"));
        }

        // The rows of one machine. With an SMBIOS GUID, a MAC address only matches rows without a GUID,
        // so a USB network adapter shared between machines does not reach the flags of the other machines.
        static string FlagWhere(string mac, string guid, uint[] ids, List<SqlParameter> ps)
        {
            List<string> or = new List<string>();
            if (guid != null)
            {
                or.Add("CAST(l.SMBIOS_GUID AS nvarchar(64)) = @guid");
                ps.Add(new SqlParameter("@guid", guid));
            }
            if (mac != null)
            {
                string m = "l.MAC_Addresses LIKE @mac";
                if (guid != null) m = "(" + m + " AND ISNULL(CAST(l.SMBIOS_GUID AS nvarchar(64)), '') IN ('', '00000000-0000-0000-0000-000000000000'))";
                or.Add(m);
                ps.Add(new SqlParameter("@mac", "%" + mac + "%"));
            }
            if (ids != null && ids.Length > 0)
            {
                List<string> list = new List<string>();
                foreach (uint id in ids) list.Add(id.ToString(CultureInfo.InvariantCulture));
                or.Add("l.MachineID IN (" + string.Join(",", list.ToArray()) + ")");
            }
            if (or.Count == 0) throw new ArgumentException("A MAC address, SMBIOS GUID or resource ID is needed.");
            return string.Join(" OR ", or.ToArray());
        }

        public List<PxeFlag> FlagsFor(string mac, string guid, uint[] ids)
        {
            List<SqlParameter> ps = new List<SqlParameter>();
            string where = FlagWhere(mac, guid, ids, ps);
            return Flags(Table("SELECT " + FlagColumns + " FROM dbo.LastPXEAdvertisement l LEFT JOIN dbo.v_R_System s ON s.ResourceID = l.MachineID WHERE " + where, ps.ToArray()));
        }

        public int DeleteFlags(string mac, string guid, uint[] ids)
        {
            List<SqlParameter> ps = new List<SqlParameter>();
            string where = FlagWhere(mac, guid, ids, ps);
            using (SqlConnection c = Open())
            using (SqlCommand cmd = new SqlCommand("DELETE l FROM dbo.LastPXEAdvertisement l WHERE " + where, c))
            {
                cmd.CommandTimeout = 60;
                cmd.Parameters.AddRange(ps.ToArray());
                return cmd.ExecuteNonQuery();
            }
        }

        // Names of the task sequences behind deployment IDs. Optional: an empty list when the views cannot be read.
        public Dictionary<string, string> DeploymentNames(ICollection<string> ids)
        {
            Dictionary<string, string> names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (ids == null || ids.Count == 0) return names;
            try
            {
                DataTable t = Table("SELECT a.AdvertisementID, p.Name FROM dbo.v_Advertisement a LEFT JOIN dbo.v_Package p ON p.PackageID = a.PackageID");
                foreach (DataRow r in t.Rows)
                {
                    string id = Str(r, "AdvertisementID");
                    if (id != null && ids.Contains(id)) names[id] = Str(r, "Name");
                }
            }
            catch (Exception) { }
            return names;
        }

        // ---------- SMS Provider ----------

        const string DeviceProps = "ResourceId, Name, MACAddresses, SMBIOSGUID, CreationDate, Client";

        static List<DeviceRecord> Devices(ManagementObjectCollection c)
        {
            List<DeviceRecord> list = new List<DeviceRecord>();
            foreach (ManagementObject o in c)
            {
                DeviceRecord d = new DeviceRecord();
                d.ResourceId = Convert.ToUInt32(o["ResourceId"], CultureInfo.InvariantCulture);
                d.Name = Convert.ToString(o["Name"]);
                string[] macs = o["MACAddresses"] as string[];
                if (macs != null) foreach (string m in macs) { string n = Ids.Mac(m); if (n != null && !d.MacAddresses.Contains(n)) d.MacAddresses.Add(n); }
                d.SmbiosGuid = Ids.Guid(Convert.ToString(o["SMBIOSGUID"]));
                string created = Convert.ToString(o["CreationDate"]);
                if (created.Length > 0)
                {
                    try { d.CreatedUtc = ManagementDateTimeConverter.ToDateTime(created).ToUniversalTime(); }
                    catch (Exception) { }
                }
                object client = o["Client"];
                d.Client = client != null && Convert.ToInt32(client, CultureInfo.InvariantCulture) == 1;
                list.Add(d);
            }
            return list;
        }

        public List<DeviceRecord> UnknownRecords()
        {
            return Devices(Query(scope, "SELECT " + DeviceProps + " FROM SMS_R_System WHERE Name = 'Unknown'"));
        }

        public List<DeviceRecord> FindDevices(string mac, string guid, string name, uint[] ids)
        {
            string where = Matching.DeviceWql(mac, guid, name, ids);
            if (where == null) return new List<DeviceRecord>();
            return Devices(Query(scope, "SELECT " + DeviceProps + " FROM SMS_R_System WHERE " + where));
        }

        public void ClearFlags(uint[] ids)
        {
            if (ids == null || ids.Length == 0) return;
            using (ManagementClass c = new ManagementClass(scope, new ManagementPath("SMS_Collection"), null))
            using (ManagementBaseObject inParams = c.GetMethodParameters("ClearLastNBSAdvForMachines"))
            {
                inParams["ResourceIDs"] = ids;
                using (ManagementBaseObject outParams = c.InvokeMethod("ClearLastNBSAdvForMachines", inParams, null))
                {
                    object rv = outParams == null ? null : outParams["ReturnValue"];
                    int code = rv == null ? 0 : Convert.ToInt32(rv, CultureInfo.InvariantCulture);
                    if (code != 0) throw new InvalidOperationException("ClearLastNBSAdvForMachines returned " + code + ".");
                }
            }
        }

        public void DeleteDevice(uint id)
        {
            using (ManagementObject o = new ManagementObject(scope, new ManagementPath("SMS_R_System.ResourceID=" + id.ToString(CultureInfo.InvariantCulture)), null))
            {
                o.Get();
                o.Delete();
            }
        }

        // ---------- PXE point ----------

        public List<string> RunningPxeServices(string server)
        {
            List<string> list = new List<string>();
            foreach (string name in PxeServices.Names)
            {
                try
                {
                    using (ServiceController s = new ServiceController(name, server))
                    {
                        if (s.Status == ServiceControllerStatus.Running) list.Add(name);
                    }
                }
                catch (InvalidOperationException) { }
            }
            return list;
        }

        public void RestartService(string server, string service)
        {
            using (ServiceController s = new ServiceController(service, server))
            {
                if (s.Status != ServiceControllerStatus.Stopped)
                {
                    s.Stop();
                    s.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(60));
                }
                s.Start();
                s.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(60));
            }
        }

        // SMSPXE.log is in SMS_DP$\sms\logs on a distribution point, and in the site's Logs folder on a site server.
        public string PxeLog(string server)
        {
            string[] candidates =
            {
                @"\\" + server + @"\SMS_DP$\sms\logs\SMSPXE.log",
                @"\\" + server + @"\SMS_" + info.SiteCode + @"\Logs\SMSPXE.log"
            };
            foreach (string c in candidates)
            {
                try { if (File.Exists(c)) return c; } catch (Exception) { }
            }
            return null;
        }

        public void Dispose()
        {
        }
    }

    // The matching rules for one machine, shared by LiveSite and DemoSite.
    public static class Matching
    {
        public static string DeviceWql(string mac, string guid, string name, uint[] ids)
        {
            List<string> or = new List<string>();
            if (guid != null) or.Add("SMBIOSGUID = '" + LiveSite.Wql(guid) + "'");
            if (mac != null)
            {
                string m = "MACAddresses = '" + LiveSite.Wql(mac) + "'";
                if (guid != null) m = "(" + m + " AND SMBIOSGUID IS NULL)";
                or.Add(m);
            }
            if (!string.IsNullOrEmpty(name)) or.Add("Name = '" + LiveSite.Wql(name) + "'");
            if (ids != null) foreach (uint id in ids) or.Add("ResourceId = " + id.ToString(CultureInfo.InvariantCulture));
            return or.Count == 0 ? null : string.Join(" OR ", or.ToArray());
        }

        public static bool Device(DeviceRecord d, string mac, string guid, string name, uint[] ids)
        {
            if (guid != null && d.SmbiosGuid == guid) return true;
            if (mac != null && d.MacAddresses.Contains(mac) && (guid == null || d.SmbiosGuid == null)) return true;
            if (!string.IsNullOrEmpty(name) && string.Equals(d.Name, name, StringComparison.OrdinalIgnoreCase)) return true;
            if (ids != null && Array.IndexOf(ids, d.ResourceId) >= 0) return true;
            return false;
        }

        public static bool Flag(PxeFlag f, string mac, string guid, uint[] ids)
        {
            if (guid != null && f.SmbiosGuid == guid) return true;
            if (mac != null && f.RawMac != null && f.RawMac.IndexOf(mac, StringComparison.OrdinalIgnoreCase) >= 0 && (guid == null || f.SmbiosGuid == null)) return true;
            if (ids != null && f.MachineId > 0 && f.MachineId <= uint.MaxValue && Array.IndexOf(ids, (uint)f.MachineId) >= 0) return true;
            return false;
        }
    }
}
