# Sends a test Windows toast, to try the island notification mirror. Run: powershell.exe -NoProfile -File scripts/send-test-toast.ps1
[void][Windows.UI.Notifications.ToastNotificationManager, Windows.UI.Notifications, ContentType = WindowsRuntime]
[void][Windows.Data.Xml.Dom.XmlDocument, Windows.Data.Xml.Dom.XmlDocument, ContentType = WindowsRuntime]
$xml = New-Object Windows.Data.Xml.Dom.XmlDocument
$xml.LoadXml('<toast duration="long"><visual><binding template="ToastGeneric"><text>Islet probe title</text><text>Islet probe body line one</text></binding></visual></toast>')
$appId = '{1AC14E77-02E7-4E5D-B744-2EB1AE5198B7}\WindowsPowerShell\v1.0\powershell.exe'
$notifier = [Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier($appId)
"setting: $($notifier.Setting)"
$notifier.Show([Windows.UI.Notifications.ToastNotification]::new($xml))
"shown"
