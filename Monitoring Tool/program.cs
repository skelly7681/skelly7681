using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Text;
using System.Collections.Generic;
using log4net;
using log4net.Config;
using System.ServiceProcess;
using System.Management;

namespace Monitoring_Tool
{
    class MonitoringTool
    {
        // Create a logger for this class
        private static readonly ILog log = LogManager.GetLogger(typeof(MonitoringTool));

        // StringBuilders to accumulate results from each method
        private static List<string> missingDailyFileResults = new List<string>();
        private static List<string> isFolderNotEmptyResults = new List<string>();
        private static List<string> fileCountsAboveThresholdResults = new List<string>();
        private static List<string> errorCheckResults = new List<string>();
        private static List<string> serviceStatusResults = new List<string>();

        private static List<string> ParseSemicolonQuoted(string field)
        {
            if (string.IsNullOrWhiteSpace(field)) return new List<string>();
            return field
                .Split(';') // split on semicolon
                .Select(s => s.Trim()) // trim spaces
                .Select(s => s.Trim('"')) // remove surrounding quotes
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToList();
        }

        static MonitoringTool()
        {
            // Configure log4net from the App.config file
            XmlConfigurator.Configure();
        }

        public static void Main(string[] args)
        {
            string logFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "StartLog.txt");
            DateTime lastStartTime;

            // Read the last start time from StartLog.txt
            if (File.Exists(logFilePath))
            {
                string lastStartTimeStr = File.ReadLines(logFilePath).LastOrDefault();
                if (!DateTime.TryParse(lastStartTimeStr.Replace("Start Time: ", ""), out lastStartTime))
                {
                    log.Warn("Unable to parse last start time from log file. Defaulting to DateTime.MinValue.");
                    lastStartTime = DateTime.MinValue;
                }
            }
            else
            {
                log.Warn("Start log file not found. Defaulting to DateTime.MinValue.");
                lastStartTime = DateTime.MinValue;
            }

            DateTime startTime = DateTime.Now;

            log.Info("Starting Monitoring Tool...");

            try
            {
                // Path to the config CSV file
                string configFilePath = System.Configuration.ConfigurationManager.AppSettings["Config_Path"];

                // Read the CSV file
                var configFileLines = File.ReadAllLines(configFilePath);

                // Iterate through each line
                foreach (var line in configFileLines)
                {
                    var columns = line.Split(',');
                    if (columns.Length < 4)
                    {
                        log.Error("Invalid config entry, missing columns");
                        continue;
                    }

                    string method = columns[0].Trim();
                    string folderPath = columns[1].Trim();
                    string isActiveFlag = columns[2].Trim().ToUpper();
                    string param = columns[3].Trim();

                    if (isActiveFlag != "Y")
                    {
                        log.Info($"Skipping inactive config entry: {method} for folder {folderPath}");
                        continue;
                    }

                    if (method.ToLower() != "services" && (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath)))
                    {
                        log.Error($"Invalid or non-existent folder path: {folderPath}");
                        continue;
                    }

                    switch (method.ToLower())
                    {
                        case "check_missing_daily_file":
                            CheckMissingDailyFile(folderPath, param, lastStartTime);
                            break;

                        case "is_folder_not_empty":
                            IsFolderNotEmpty(folderPath);
                            break;

                        case "check_file_counts_above_threshold":
                            if (int.TryParse(param, out int fileLimit))
                            {
                                CheckFileCountsAboveThreshold(folderPath, fileLimit);
                            }
                            else
                            {
                                log.Error($"Invalid file limit value for method {method} in config: {param}");
                            }

                            break;

                        case "check_error":
                        {
                            string path = columns[1].Trim();
                            string exceptionsField = columns.Length > 3 ? columns[3] : string.Empty;

                            var exceptions = ParseSemicolonQuoted(exceptionsField);
                            if (exceptions.Count > 0)
                                CheckErrorEx(path, lastStartTime, exceptions);
                            else
                                CheckError(path, lastStartTime);
                            break;
                        }

                        case "services":
                            CheckWindowsService(folderPath, param);
                            break;

                        default:
                            log.Error($"Unknown method in config: {method}");
                            break;
                    }
                }

                // Group results and send mail
                StringBuilder emailBody = new StringBuilder();
                emailBody.AppendLine("<h2>Monitoring Tool Results</h2>");

                if (missingDailyFileResults.Count > 0)
                {
                    emailBody.AppendLine("<h2>Check Missing Daily File</h2>");
                    emailBody.AppendLine("<table border='1'>");
                    emailBody.AppendLine("<tr><th>Folder Path</th><th>Date</th><th>File Count</th></tr>");
                    foreach (var result in missingDailyFileResults)
                    {
                        emailBody.AppendLine(result);
                    }

                    emailBody.AppendLine("</table>");
                }

                if (isFolderNotEmptyResults.Count > 0)
                {
                    emailBody.AppendLine("<h2>Is Folder Not Empty</h2>");
                    emailBody.AppendLine("<table border='1'>");
                    emailBody.AppendLine("<tr><th>Folder Path</th><th>File Name</th></tr>");
                    foreach (var result in isFolderNotEmptyResults)
                    {
                        emailBody.AppendLine(result);
                    }

                    emailBody.AppendLine("</table>");
                }

                if (fileCountsAboveThresholdResults.Count > 0)
                {
                    emailBody.AppendLine("<h2>Check File Counts Above Threshold</h2>");
                    emailBody.AppendLine("<table border='1'>");
                    emailBody.AppendLine("<tr><th>Folder Path</th><th>Threshold</th><th>File Count</th></tr>");
                    foreach (var result in fileCountsAboveThresholdResults)
                    {
                        emailBody.AppendLine(result);
                    }

                    emailBody.AppendLine("</table>");
                }

                if (errorCheckResults.Count > 0)
                {
                    emailBody.AppendLine("<h2>Check Error</h2>");
                    emailBody.AppendLine("<table border='1'>");
                    emailBody.AppendLine("<tr><th>Folder Path</th><th>File Name</th><th>Error Details</th></tr>");
                    foreach (var result in errorCheckResults)
                    {
                        emailBody.AppendLine(result);
                    }

                    emailBody.AppendLine("</table>");
                }

                if (serviceStatusResults.Count > 0)
                {
                    emailBody.AppendLine("<h2>Windows Service Status</h2>");
                    emailBody.AppendLine("<table border='1'>");
                    emailBody.AppendLine("<tr><th>Service Name</th><th>Status</th><th>Startup Type</th></tr>");
                    foreach (var result in serviceStatusResults)
                    {
                        emailBody.AppendLine(result);
                    }

                    emailBody.AppendLine("</table>");
                }

                if (missingDailyFileResults.Count > 0 ||
                    isFolderNotEmptyResults.Count > 0 ||
                    fileCountsAboveThresholdResults.Count > 0 ||
                    errorCheckResults.Count > 0 ||
                    serviceStatusResults.Count > 0)
                {
                    SendMail(emailBody.ToString());
                    log.Info("Email sent with monitoring results.");
                }
                else
                {
                    log.Info("No issues found. Email not sent.");
                }
            }
            catch (Exception ex)
            {
                log.Error("Error occurred in Monitoring Tool", ex);
            }

            File.WriteAllText(logFilePath, $"Start Time: {startTime:yyyy-MM-dd HH:mm:ss.fff}{Environment.NewLine}");
            log.Info("Monitoring Tool finished.");
        }

        public static void CheckMissingDailyFile(string folderPath, string time, DateTime lastStartTime)
        {
            DateTime currentDate = DateTime.Now.Date;
            if (!Directory.Exists(folderPath))
            {
                log.Error($"Folder path {folderPath} does not exist.");
                return;
            }

            if (DateTime.TryParse(time, out DateTime specifiedTime))
            {
                DateTime combinedSpecifiedTime = currentDate.Add(specifiedTime.TimeOfDay);
                if (lastStartTime.Date == currentDate && combinedSpecifiedTime <= lastStartTime)
                {
                    log.Info($"Skipping CheckMissingDailyFile for {folderPath} as the specified time {time} has not occurred since the last start.");
                    return;
                }
            }
            else
            {
                log.Error($"Invalid time format in config: {time}");
                return;
            }

            var filesFromToday = Directory.GetFiles(folderPath).Where(file => File.GetCreationTime(file).Date == currentDate).ToList();
            if (filesFromToday.Count == 0)
            {
                missingDailyFileResults.Add($"<tr><td>{folderPath}</td><td>{currentDate.ToShortDateString()}</td><td>{filesFromToday.Count}</td></tr>");
                log.Info($"No files found from today in {folderPath}");
            }
            else
            {
                log.Info($"Files found from today in {folderPath}: {filesFromToday.Count}");
            }
        }

        public static void IsFolderNotEmpty(string folderPath)
        {
            log.Info($"Checking if folder {folderPath} is not empty...");
            if (!Directory.Exists(folderPath))
            {
                log.Error($"Folder path {folderPath} does not exist.");
                return;
            }

            var files = Directory.GetFiles(folderPath).ToList();
            if (files.Count > 0)
            {
                foreach (var file in files)
                {
                    string fileName = Path.GetFileName(file);
                    isFolderNotEmptyResults.Add($"<tr><td>{folderPath}</td><td>{fileName}</td></tr>");
                }

                log.Info($"Folder {folderPath} contains {files.Count} file(s).");
            }
            else
            {
                log.Info($"Folder {folderPath} is empty.");
            }
        }

        public static void CheckFileCountsAboveThreshold(string folderPath, int fileLimit)
        {
            log.Info($"Checking if file counts in {folderPath} exceed {fileLimit}...");
            if (!Directory.Exists(folderPath))
            {
                log.Error($"Folder path {folderPath} does not exist.");
                return;
            }

            var files = Directory.GetFiles(folderPath).ToList();
            if (files.Count > fileLimit)
            {
                fileCountsAboveThresholdResults.Add($"<tr><td>{folderPath}</td><td>{fileLimit}</td><td>{files.Count}</td></tr>");
                log.Info($"Folder {folderPath} contains {files.Count} file(s), which exceeds the threshold of {fileLimit}.");
            }
            else
            {
                log.Info($"Folder {folderPath} contains {files.Count} file(s), which does not exceed the threshold of {fileLimit}.");
            }
        }

        public static void CheckError(string folderPath, DateTime lastStartTime)
        {
            log.Info($"Checking for errors in {folderPath}...");
            if (!Directory.Exists(folderPath))
            {
                log.Error($"Folder path {folderPath} does not exist.");
                return;
            }

            var filesToCheck = Directory.GetFiles(folderPath)
                                        .Where(file => File.GetLastWriteTime(file) > lastStartTime)
                                        .ToList();
            foreach (var file in filesToCheck)
            {
                log.Info($"Checking file: {file}");
                bool errorFound = false;
                StringBuilder errorDetails = new StringBuilder();
                try
                {
                    using (var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    using (var reader = new StreamReader(stream))
                    {
                        string line;
                        while ((line = reader.ReadLine()) != null)
                        {
                            DateTime lineTimestamp = DateTime.MinValue;
                            bool hasTimestamp = line.Length >= 19 && DateTime.TryParse(line.Substring(0, 19), out lineTimestamp);

                            if (hasTimestamp && lineTimestamp < lastStartTime)
                                continue;

                            if (line.Contains("ERROR"))
                            {
                                if (errorFound)
                                {
                                    errorCheckResults.Add($"<tr><td>{folderPath}</td><td>{Path.GetFileName(file)}</td><td>{errorDetails}</td></tr>");
                                    log.Info($"Issue found in {file}");
                                    errorDetails.Clear();
                                }

                                errorFound = true;
                                errorDetails.AppendLine(line);
                            }
                            else if (errorFound)
                            {
                                if (hasTimestamp)
                                {
                                    errorCheckResults.Add($"<tr><td>{folderPath}</td><td>{Path.GetFileName(file)}</td><td>{errorDetails}</td></tr>");
                                    log.Info($"Issue found in {file}");
                                    errorDetails.Clear();
                                    errorFound = false;
                                }
                                else
                                {
                                    errorDetails.AppendLine(line);
                                }
                            }
                        }

                        if (errorFound)
                        {
                            errorCheckResults.Add($"<tr><td>{folderPath}</td><td>{Path.GetFileName(file)}</td><td>{errorDetails}</td></tr>");
                            log.Info($"Issue found in {file}");
                        }
                    }
                }
                catch (IOException ioEx)
                {
                    log.Warn($"Skipping file due to IO error: {file} - {ioEx.Message}");
                }
                catch (Exception ex)
                {
                    log.Warn($"Unexpected error in file: {file} - {ex.Message}");
                }
            }
        }

        public static void CheckErrorEx(string folderPath, DateTime lastStartTime, List<string> fileExceptions)
        {
            log.Info($"Checking for errors in {folderPath} with file exceptions...");
            if (!Directory.Exists(folderPath))
            {
                log.Error($"Folder path {folderPath} does not exist.");
                return;
            }

            var filesToCheck = Directory.GetFiles(folderPath)
                                        .Where(file => File.GetLastWriteTime(file) > lastStartTime)
                                        .ToList();
            foreach (var file in filesToCheck)
            {
                log.Info($"Checking file: {file}");
                try
                {
                    using (var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    using (var reader = new StreamReader(stream))
                    {
                        string line;
                        while ((line = reader.ReadLine()) != null)
                        {
                            DateTime lineTimestamp = DateTime.MinValue;
                            bool hasTimestamp = line.Length >= 19 && DateTime.TryParse(line.Substring(0, 19), out lineTimestamp);

                            if (hasTimestamp && lineTimestamp < lastStartTime)
                                continue;

                            if (line.Contains("ERROR") && !fileExceptions.Any(exception => line.Contains(exception)))
                            {
                                errorCheckResults.Add($"<tr><td>{folderPath}</td><td>{Path.GetFileName(file)}</td><td>{line}</td></tr>");
                                log.Info($"Issue found in {file}");
                            }
                        }
                    }
                }
                catch (IOException ioEx)
                {
                    log.Warn($"Skipping file due to IO error: {file} - {ioEx.Message}");
                }
                catch (Exception ex)
                {
                    log.Warn($"Unexpected error in file: {file} - {ex.Message}");
                }
            }
        }

        public static void CheckWindowsService(string serviceName, string param)
        {
            log.Info($"Checking Windows service: {serviceName} with parameters: {param}");
            try
            {
                string[] parameters = param.Split('|');
                string expectedStatus = parameters.Length > 0 ? parameters[0].Trim() : "";
                string expectedStartupType = parameters.Length > 1 ? parameters[1].Trim() : "";

                using (var sc = new ServiceController(serviceName))
                {
                    string actualStatus = sc.Status.ToString();
                    string actualStartupType = "Unknown";

                    string query = $"SELECT * FROM Win32_Service WHERE Name = '{serviceName}'";
                    using (var searcher = new ManagementObjectSearcher(query))
                    {
                        foreach (ManagementObject service in searcher.Get())
                        {
                            actualStartupType = service["StartMode"]?.ToString();
                        }
                    }

                    bool statusMismatch = !string.IsNullOrEmpty(expectedStatus) && actualStatus != expectedStatus;
                    bool startupTypeMismatch = !string.IsNullOrEmpty(expectedStartupType) && actualStartupType != expectedStartupType;

                    if (statusMismatch || startupTypeMismatch)
                    {
                        serviceStatusResults.Add($"<tr><td>{serviceName}</td><td>{actualStatus}{(statusMismatch ? " (Expected: " + expectedStatus + ")" : string.Empty)}</td><td>{actualStartupType}{(startupTypeMismatch ? " (Expected: " + expectedStartupType + ")" : string.Empty)}</td></tr>");
                        log.Info($"Service {serviceName} has status {actualStatus} (expected {expectedStatus}) and startup type {actualStartupType} (expected {expectedStartupType})");
                    }
                    else
                    {
                        log.Info($"Service {serviceName} matches expected configuration. Status: {actualStatus}, Startup Type: {actualStartupType}");
                    }
                }
            }
            catch (Exception ex)
            {
                log.Error($"Failed to retrieve status for service '{serviceName}': {ex.Message}");
                serviceStatusResults.Add($"<tr><td>{serviceName}</td><td colspan='2'>Error: {ex.Message}</td></tr>");
            }
        }

        public static void SendMail(string body)
        {
            string fromEmail = System.Configuration.ConfigurationManager.AppSettings["EmailFrom"];
            string toEmail = System.Configuration.ConfigurationManager.AppSettings["EmailTo"];
            string smtpServer = System.Configuration.ConfigurationManager.AppSettings["SMTP_SERVER"];
            string smtpUser = System.Configuration.ConfigurationManager.AppSettings["EmailFrom"];
            string smtpPass = System.Configuration.ConfigurationManager.AppSettings["SMTP_PASS"];

            MailMessage mail = new MailMessage(fromEmail, toEmail)
            {
                Subject = "Monitoring Tool Report",
                Body = body,
                IsBodyHtml = true
            };

            SmtpClient smtp = new SmtpClient(smtpServer)
            {
                Port = Convert.ToInt32(System.Configuration.ConfigurationManager.AppSettings["SMTP_PORT"]),
                Credentials = new NetworkCredential(smtpUser, smtpPass),
                EnableSsl = true
            };

            int retryCount = 3;
            for (int attempt = 1; attempt <= retryCount; attempt++)
            {
                try
                {
                    log.Info($"SMTP config: Server={smtpServer}, Port={smtp.Port}, From={fromEmail}, To={toEmail}, SSL={smtp.EnableSsl}");

                    smtp.Send(mail);
                    log.Info("Email sent successfully.");
                    break;
                }
                catch (SmtpException smtpEx)
                {
                    log.Error($"Attempt {attempt} - SMTP error code: {smtpEx.StatusCode}. Message: {smtpEx.Message}", smtpEx);
                    if (attempt == retryCount)
                    {
                        log.Error("Maximum retry attempts reached. Could not send email.");
                    }
                }
                catch (Exception ex)
                {
                    log.Error($"Attempt {attempt} - Error sending email", ex);
                    if (attempt == retryCount)
                    {
                        log.Error("Maximum retry attempts reached. Could not send email.");
                    }
                }
            }
        }
    }
}
