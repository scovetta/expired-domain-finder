using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;
using Whois.Net;

namespace ExpiredDomainFinder
{
    public partial class MainForm : Form
    {
        private static readonly string VERSION = "0.1";

        private int NumPackagesComplete = 0;
        
        private ConcurrentBag<Row> Rows = null;

        private static readonly HttpClient _HttpClient = new()
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        private readonly ConcurrentDictionary<string, DateTime?> DnsCache = new();

        private readonly Whois.WhoisLookup _WhoisLookup = new(new Whois.WhoisOptions()
        {
            TimeoutSeconds = 30,
            FollowReferrer = true
        });

        public MainForm()
        {
            InitializeComponent();

            // See if Docker is available
            try
            {
                using Process process = new();
                process.StartInfo.FileName = "docker";
                process.StartInfo.ArgumentList.Add("version");
                process.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
                process.StartInfo.CreateNoWindow = true;
                process.Start();
                process.WaitForExit();
                checkBox2.Checked = true;
            }
            catch(Exception)
            {
                checkBox2.Checked = false;
                checkBox2.Enabled = false;
            }

            // See if whois.exe is available
            try
            {
                using Process process = new();
                process.StartInfo.FileName = "whois";
                process.StartInfo.ArgumentList.Add("-v");
                process.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
                process.StartInfo.CreateNoWindow = true;
                process.Start();
                process.WaitForExit();
                checkBox3.Checked = true;
                textBox1.Text = "whois";
            }
            catch (Exception)
            {
                checkBox3.Checked = false;
                checkBox3.Enabled = false;
            }


        }

        private void button1_Click(object sender, EventArgs e)
        {
            if (startCancelButton.Text == "Start")
            {
                if (!checkBox1.Checked && !checkBox2.Checked && !checkBox3.Checked)
                {
                    MessageBox.Show("No whois provider has been selected.", "Choose a whois provider", MessageBoxButtons.OK);
                    return;
                }
                startCancelButton.Text = "Cancel";

                var lines = inputPackageTextBox.Text.Split(new char[] { '\n' });
                dataGridView1.Rows.Clear();
                dataGridView1.Sort(dataGridView1.Columns[2], ListSortDirection.Descending);

                toolStripProgressBar.Minimum = 0;
                toolStripProgressBar.Maximum = lines.Length;
                toolStripLabel.Text = $"Analyzing {lines.Length} packages...";

                backgroundWorker1.RunWorkerAsync(lines);
            }
            else
            {
                backgroundWorker1.CancelAsync();
            }
        }

        private async Task<DateTime?> GetExpiration(string domain)
        {
            if (!DnsCache.ContainsKey(domain))
            {
                var success = false;
                if (checkBox1.Checked)
                {
                    var result = await _WhoisLookup.LookupAsync(domain);
                    DnsCache[domain] = result.Expiration;
                    success = result.Expiration.HasValue;
                }

                if (!success && checkBox2.Checked)
                {
                    try
                    {
                        using Process process = new();
                        process.StartInfo.FileName = "docker";
                        process.StartInfo.ArgumentList.Add("run");
                        process.StartInfo.ArgumentList.Add("--rm");
                        process.StartInfo.ArgumentList.Add("dentych/whois");
                        process.StartInfo.ArgumentList.Add(domain);
                        process.StartInfo.RedirectStandardOutput = true;
                        process.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
                        process.StartInfo.CreateNoWindow = true;
                        process.Start();

                        var dockerResult = process.StandardOutput.ReadToEnd();
                        foreach (var line in dockerResult.Split("\n"))
                        {
                            try
                            {
                                if (line.Contains("Registry Expiry Date:") ||
                                    line.Contains("Expiration Date:") ||
                                    line.Contains("Domain expires:"))
                                {
                                    var date = line.Split(':', 2)[1].Trim();
                                    var dateObject = DateTime.Parse(date);
                                    DnsCache[domain] = dateObject;
                                    success = true;
                                    break;
                                }
                            }
                            catch (Exception)
                            {
                                DnsCache[domain] = null;
                                success = false;
                            }
                        }
                    }
                    catch(Exception)
                    {
                        DnsCache[domain] = null;
                        success = false;
                    }
                }

                if (!success && checkBox3.Checked)
                {
                    using Process process = new();
                    
                    process.StartInfo.FileName = textBox1.Text;
                    process.StartInfo.ArgumentList.Add(domain);
                    process.StartInfo.RedirectStandardOutput = true;
                    process.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
                    process.StartInfo.CreateNoWindow = true;
                    process.Start();

                    var dockerResult = process.StandardOutput.ReadToEnd();
                    foreach (var line in dockerResult.Split("\n"))
                    {
                        try
                        {
                            if (line.Contains("Registry Expiry Date:") ||
                                line.Contains("Expiration Date:") ||
                                line.Contains("Domain expires:"))
                            {
                                var date = line.Split(':', 2)[1].Trim();
                                var dateObject = DateTime.Parse(date);
                                DnsCache[domain] = dateObject;
                                success = true;
                                break;
                            }
                        }
                        catch (Exception)
                        {
                            DnsCache[domain] = null;
                            success = false;
                        }
                    }
                }
            }
            return DnsCache.ContainsKey(domain) ? DnsCache[domain] : null;
        }

        private async Task<IEnumerable<string>> GetDomains(string packageName)
        {
            if (string.IsNullOrWhiteSpace(packageName))
            {
                return Array.Empty<string>();
            }
            HttpResponseMessage result = null;
            try
            {
                result = await _HttpClient.GetAsync($"https://registry.npmjs.com/{packageName}");
            }
            catch (Exception)
            {
                return Array.Empty<string>();
            }

            if (result.IsSuccessStatusCode)
            {
                var domains = new HashSet<string>();
                try
                {
                    var doc = await JsonDocument.ParseAsync(await result.Content.ReadAsStreamAsync());
                    var root = doc.RootElement;
                    var latest = root.GetProperty("dist-tags").GetProperty("latest").GetString();
                    var version = root.GetProperty("versions").GetProperty(latest);

                    var npmUser = version.GetProperty("_npmUser").GetProperty("email").GetString();
                    domains.Add(GetDomainFromString(npmUser));

                    foreach (var maintainer in version.GetProperty("maintainers").EnumerateArray())
                    {
                        try
                        {
                            domains.Add(GetDomainFromString(maintainer.GetProperty("email").GetString()));
                        }
                        catch (Exception)
                        {
                            // pass
                        }
                    }
                }
                catch (Exception)
                {
                    // pass
                }
                return domains;
            }
            return Array.Empty<string>();
        }

        private string GetDomainFromString(string s)
        {
            var parts = s.Split(new char[] { '@' });
            return parts[^1];
        }

        private void button2_Click(object sender, EventArgs e)
        {
            backgroundWorker1.CancelAsync();
            Environment.Exit(1);
        }

        private void backgroundWorker1_DoWork(object sender, DoWorkEventArgs e)
        {
            var worker = sender as BackgroundWorker;

            var lines = (IEnumerable<string>)e.Argument;
            int numPackages = lines.Count();

            Rows = new ConcurrentBag<Row>();
            
            foreach (var line in lines)
            {
                var domains = GetDomains(line.Trim()).Result;
                foreach (var domain in domains)
                {
                    if (worker.CancellationPending)
                    {
                        return;
                    }

                    bool success = false;
                    int numAttemptsLeft = 2;
                    while (!success && numAttemptsLeft-- > 0)
                    {
                        try
                        {

                            DateTime? expiration = GetExpiration(domain).Result;
                            var expirationString = expiration.HasValue ? expiration.Value.ToString("yyyy-MM-dd") : "Unknown";
                            Rows.Add(new Row()
                            {
                                Package = line,
                                Domain = domain,
                                Expiration = expirationString
                            });
                            success = true;
                            backgroundWorker1.ReportProgress(0);
                        }
                        catch (Exception)
                        {
                            // We'll just try again
                        }
                    }
                    
                    // We weren't able to find an expiration date.
                    if (!success)
                    {
                        Rows.Add(new Row()
                        {
                            Package = line,
                            Domain = domain,
                            Expiration = "Error"
                        });
                    }

                    NumPackagesComplete++;
                }
                backgroundWorker1.ReportProgress(0);
            }
        }

        private void backgroundWorker1_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            if (e.Error != null)
            {
                toolStripLabel.Text = "Error: " + e.Error.Message;
            }
            else if (e.Cancelled)
            {
                toolStripLabel.Text = "Operation cancelled.";
            }
            else
            {
                toolStripLabel.Text = "Operation complete.";
            }
            toolStripProgressBar.Value = 0;
            startCancelButton.Text = "Start";
        }

        private void backgroundWorker1_ProgressChanged(object sender, ProgressChangedEventArgs e)
        {
            toolStripProgressBar.Value = Math.Min(toolStripProgressBar.Maximum, NumPackagesComplete);
            while (Rows.TryTake(out Row row))
            {
                dataGridView1.Rows.Add(row.Package, row.Domain, row.Expiration);
            }
        }

        private void exitToolStripMenuItem_Click(object sender, EventArgs e)
        {
            backgroundWorker1.CancelAsync();
            Environment.Exit(0);
        }

        private void button1_Click_1(object sender, EventArgs e)
        {
            DnsCache.Clear();
        }

        private void aboutToolStripMenuItem_Click(object sender, EventArgs e)
        {
            MessageBox.Show($"Expired Domain Finder v{VERSION}.\n\nFor more information, visit github.com/scovetta/expired-domain-finder.", "About", MessageBoxButtons.OK);
        }
    }
}