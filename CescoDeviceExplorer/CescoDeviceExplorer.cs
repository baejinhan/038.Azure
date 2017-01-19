using System;
using System.IO;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using DevExpress.XtraGrid;
using Microsoft.Azure.Devices;
using Microsoft.Azure.Devices.Common;
using System.Threading;
using Microsoft.ServiceBus.Messaging;

namespace CescoDeviceExplorer
{
    public partial class CescoDeviceExplorer : Form
    {
        string IOTHubConnectionString = string.Empty;
        static RegistryManager registryManager;
        readonly static string FilePath = GetPropertiesString("DeviceList");
        private static CancellationTokenSource ctsForDataMonitoring;
        private static CancellationTokenSource ctsForFeedbackMonitoring;
        private static int eventHubPartitionsCount;

        public CescoDeviceExplorer()
        {
            InitializeComponent();
        }
        private static void SetRegistryManager(string IOTHubConnectionString)
        {
            registryManager = RegistryManager.CreateFromConnectionString(IOTHubConnectionString);
        }
        private void Connection_Click(object sender, EventArgs e)
        {
            if (Connection.Text == "해지")
            {
                Connection.Text = "연결";
                IotHubName.Enabled = true;
                if (!dataMonitorButton.Enabled)
                    cancelMonitoring();
                panelContainer1.Enabled = false;
                DeviceID.Text = string.Empty;
                gridControl1.DataSource = null;
                if (checkBoxMonitorFeedbackEndpoint.CheckState == CheckState.Checked)
                    checkBoxMonitorFeedbackEndpoint.CheckState = CheckState.Unchecked;
                MessageCountTextBox.Text = string.Empty;

            }
            else
            {
                Connection.Text = "해지";
                IotHubName.Enabled = false;
                IOTHubConnectionString = GetPropertiesString(IotHubName.Text);
                SetRegistryManager(IOTHubConnectionString);
                panelContainer1.Enabled = true;
                gridControl1.DataSource = null;
                if (File.Exists(FilePath))
                    gridControl1.DataSource = GetDeviceList(IotHubName.Text);
            }

        }
        private static DataTable GetDeviceList(string IOTHubName)
        {
            var JsonDeserialize = GetReaderJson(FilePath);
            var Devices = JsonDeserialize[IOTHubName];
            if (Devices == null)
                return null;
            var stuff = Convert.ToString(Devices).Split(',').Select(a => new { DeviceID = a }).ToList();
            return ConvertToDataTable(stuff);
        }
        public static DataTable ConvertToDataTable<T>(IList<T> data)
        {
            PropertyDescriptorCollection properties =
               TypeDescriptor.GetProperties(typeof(T));
            DataTable table = new DataTable();
            foreach (PropertyDescriptor prop in properties)
                table.Columns.Add(prop.Name, Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType);
            foreach (T item in data)
            {
                DataRow row = table.NewRow();
                foreach (PropertyDescriptor prop in properties)
                    row[prop.Name] = prop.GetValue(item) ?? DBNull.Value;
                table.Rows.Add(row);
            }
            DataRow[] rows = table.Select("DeviceID <> ''");
            if (rows.Length > 0)
                return rows.CopyToDataTable();
            else
                return null;
        }

        private static string GetPropertiesString(string PropertiesName)
        {
            return Properties.Settings.Default.Properties[PropertiesName].DefaultValue.ToString();
        }
        private static JObject GetReaderJson(string FilePath)
        {
            return JObject.Parse(JsonConvert.DeserializeObject(File.ReadAllText(FilePath)).ToString());
        }

        private async void DeviceIDSave_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(DeviceID.Text.Trim()))
            {
                MessageBox.Show("ID를 입력하여 주십시오.", "확인", MessageBoxButtons.OK, MessageBoxIcon.Information);
                DeviceID.Focus();
                return;
            }
            var rowHandle = gridView1.LocateByValue("DeviceID", DeviceID.Text.Trim());
            if (rowHandle >= 0)
            {
                MessageBox.Show("이미 등록된 ID입니다.", "확인", MessageBoxButtons.OK, MessageBoxIcon.Information);
                DeviceID.Focus();
                return;
            }


            bool check = await GetDevice(DeviceID.Text.Trim());
            if (!check)
            {
                MessageBox.Show("등록되지 않은 장치입니다.", "확인", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            JsonWrite(DeviceID.Text.Trim(), IotHubName.Text);
            gridControl1.DataSource = GetDeviceList(IotHubName.Text);
            DeviceID.Text = string.Empty;
        }


        private static void JsonWrite(string DeviceID, string IOTHubName)
        {
            if (string.IsNullOrEmpty(FilePath)) return;

            string FolderPath = Path.GetDirectoryName(FilePath);
            if (!Directory.Exists(FolderPath))
            {
                Directory.CreateDirectory(FolderPath).Attributes = FileAttributes.Hidden;
                var Json = new JObject();
                Json.Add(IOTHubName, DeviceID);
                File.WriteAllText(FilePath, Json.ToString());
            }
            else
            {
                var JsonDeserialize = GetReaderJson(FilePath);
                var Devices = JsonDeserialize[IOTHubName];
                if (string.IsNullOrEmpty(Convert.ToString(Devices)))
                    Devices = string.Format("{0}", DeviceID);
                else
                    Devices = string.Format("{0},{1}", Devices, DeviceID);

                JsonDeserialize[IOTHubName] = Devices;
                File.WriteAllText(FilePath, JsonDeserialize.ToString());
            }
        }

        private void repositoryItemButtonEdit1_ButtonClick(object sender, DevExpress.XtraEditors.Controls.ButtonPressedEventArgs e)
        {
            JsonDelete(gridView1.GetFocusedRowCellDisplayText("DeviceID"), IotHubName.Text);
            gridControl1.DataSource = GetDeviceList(IotHubName.Text);
        }
        private static void JsonDelete(string DeviceID, string IOTHubName)
        {
            var JsonDeserialize = GetReaderJson(FilePath);
            var Devices = JsonDeserialize[IOTHubName];
            Devices = Devices.ToString().Replace(DeviceID, "");
            JsonDeserialize[IOTHubName] = Devices;
            File.WriteAllText(FilePath, JsonDeserialize.ToString());
        }

        public Task<bool> GetDevice(string DeviceID)
        {
            return GetDeviceAsync(DeviceID);
        }

        private static async Task<bool> GetDeviceAsync(string DeviceID)
        {
            bool Check = false;
            Device device = await registryManager.GetDeviceAsync(DeviceID);
            if (device != null)
                Check = true;

            return Check;
        }

        private void gridControl1_DataSourceChanged(object sender, EventArgs e)
        {
            DataTable Table = ((DataTable)gridControl1.DataSource);
            deviceIDsComboBoxForEvent.Properties.DataSource = Table;
            deviceIDsComboBoxForCloudToDeviceMessage.Properties.DataSource = Table;
            if (gridView1.RowCount > 0)
            {
                string value = gridView1.GetRowCellValue(0, "DeviceID").ToString();
                deviceIDsComboBoxForEvent.EditValue = value;
                deviceIDsComboBoxForCloudToDeviceMessage.EditValue = value;
            }
        }

        private void dataMonitorButton_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(deviceIDsComboBoxForEvent.Text))
            {
                MessageBox.Show("ID를 선택하여 주십시오.", "확인", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            deviceIDsComboBoxForEvent.Enabled = false;
            cancelMonitoringButton.Enabled = true;
            dataMonitorButton.Enabled = false;
            ctsForDataMonitoring = new CancellationTokenSource();
            if (dateTimePicker.Checked == false)
            {
                dateTimePicker.Value = DateTime.Now;
                dateTimePicker.Checked = false;
            }

            MonitorEventHubAsync(dateTimePicker.Value, ctsForDataMonitoring.Token, "$Default", DateTime.Now, dateTimePicker.Checked);

        }
        private async void MonitorEventHubAsync(DateTime startTime, CancellationToken ct, string consumerGroupName, DateTime NowDateTime, bool dateTimePickerChecked)
        {
            EventHubClient eventHubClient = null;
            EventHubReceiver eventHubReceiver = null;

            try
            {
                string selectedDevice = deviceIDsComboBoxForEvent.Text;
                eventHubClient = EventHubClient.CreateFromConnectionString(IOTHubConnectionString, "messages/events");
                eventHubTextBox.Text = "Receiving events...\r\n";
                WriteTextLog(string.Format("{0} : 모니터링 시작", NowDateTime), selectedDevice);
                eventHubPartitionsCount = eventHubClient.GetRuntimeInformation().PartitionCount;
                string partition = EventHubPartitionKeyResolver.ResolveToPartition(selectedDevice, eventHubPartitionsCount);
                eventHubReceiver = eventHubClient.GetConsumerGroup(consumerGroupName).CreateReceiver(partition, startTime);

                //receive the events from startTime until current time in a single call and process them
                var events = await eventHubReceiver.ReceiveAsync(int.MaxValue, TimeSpan.FromSeconds(20));

                foreach (var eventData in events)
                {
                    var data = Encoding.UTF8.GetString(eventData.GetBytes());
                    var enqueuedTime = eventData.EnqueuedTimeUtc.ToLocalTime();
                    var connectionDeviceId = eventData.SystemProperties["iothub-connection-device-id"].ToString();

                    if (string.CompareOrdinal(selectedDevice.ToUpper(), connectionDeviceId.ToUpper()) == 0)
                    {
                        if (!(enqueuedTime < NowDateTime && dateTimePickerChecked))
                            eventHubTextBox.Text += $"{enqueuedTime}> Device: [{connectionDeviceId}], Data:[{data}]";

                        WriteTextLog($"{enqueuedTime}> Device: [{connectionDeviceId}], Data:[{data}]", connectionDeviceId);
                        if (eventData.Properties.Count > 0)
                        {
                            if (!(enqueuedTime < NowDateTime && dateTimePickerChecked))
                                eventHubTextBox.Text += "Properties:\r\n";

                            WriteTextLog("Properties:\r\n", connectionDeviceId);
                            foreach (var property in eventData.Properties)
                            {
                                if (!(enqueuedTime < NowDateTime && dateTimePickerChecked))
                                    eventHubTextBox.Text += $"'{property.Key}': '{property.Value}'\r\n";
                                WriteTextLog($"'{property.Key}': '{property.Value}'\r\n", connectionDeviceId);
                            }
                        }
                        if (!(enqueuedTime < NowDateTime && dateTimePickerChecked))
                            eventHubTextBox.Text += "\r\n";

                        // scroll text box to last line by moving caret to the end of the text
                        if (!(enqueuedTime < NowDateTime && dateTimePickerChecked))
                        {
                            //eventHubTextBox.SelectionStart = eventHubTextBox.Text.Length - 1;
                            eventHubTextBox.SelectionStart = Int32.MaxValue;
                            eventHubTextBox.ScrollToCaret();
                        }
                    }
                }
                //having already received past events, monitor current events in a loop
                while (true)
                {
                    ct.ThrowIfCancellationRequested();

                    var eventData = await eventHubReceiver.ReceiveAsync(TimeSpan.FromSeconds(1));

                    if (eventData != null)
                    {
                        var data = Encoding.UTF8.GetString(eventData.GetBytes());
                        var enqueuedTime = eventData.EnqueuedTimeUtc.ToLocalTime();

                        // Display only data from the selected device; otherwise, skip.
                        var connectionDeviceId = eventData.SystemProperties["iothub-connection-device-id"].ToString();

                        if (string.CompareOrdinal(selectedDevice, connectionDeviceId) == 0)
                        {
                            if (!(enqueuedTime < NowDateTime && dateTimePickerChecked))
                                eventHubTextBox.Text += $"{enqueuedTime}> Device: [{connectionDeviceId}], Data:[{data}]";

                            WriteTextLog($"{enqueuedTime}> Device: [{connectionDeviceId}], Data:[{data}]", connectionDeviceId);
                            if (eventData.Properties.Count > 0)
                            {
                                if (!(enqueuedTime < NowDateTime && dateTimePickerChecked))
                                    eventHubTextBox.Text += "Properties:\r\n";
                                WriteTextLog("Properties:\r\n", connectionDeviceId);
                                foreach (var property in eventData.Properties)
                                {
                                    if (!(enqueuedTime < NowDateTime && dateTimePickerChecked))
                                        eventHubTextBox.Text += $"'{property.Key}': '{property.Value}'\r\n";
                                    WriteTextLog($"'{property.Key}': '{property.Value}'\r\n", connectionDeviceId);
                                }
                            }
                            if (!(enqueuedTime < NowDateTime && dateTimePickerChecked))
                                eventHubTextBox.Text += "\r\n";
                        }

                        // scroll text box to last line by moving caret to the end of the text
                        if (!(enqueuedTime < NowDateTime && dateTimePickerChecked))
                        {
                            eventHubTextBox.SelectionStart = Int32.MaxValue;
                            eventHubTextBox.ScrollToCaret();
                            labelControl3.Text = "";
                        }
                        else
                        {
                            labelControl3.Text = "이전 로그를 읽고 있습니다.... (처리 완료 후 실시간 모니터링 데이터가 표시됩니다.)";
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                if (ct.IsCancellationRequested)
                {
                    eventHubTextBox.Text += $"Stopped Monitoring events. {ex.Message}\r\n";
                }
                else
                {
                    using (new CenterDialog(this))
                    {
                        MessageBox.Show(ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                    eventHubTextBox.Text += $"Stopped Monitoring events. {ex.Message}\r\n";
                }
                if (eventHubReceiver != null)
                {
                    eventHubReceiver.Close();
                }
                if (eventHubClient != null)
                {
                    eventHubClient.Close();
                }
            }
        }

        private void clearDataButton_Click(object sender, EventArgs e)
        {

        }

        private void cancelMonitoringButton_Click(object sender, EventArgs e)
        {
            cancelMonitoring();
        }
        private void cancelMonitoring()
        {
            dataMonitorButton.Enabled = true;
            cancelMonitoringButton.Enabled = false;
            deviceIDsComboBoxForEvent.Enabled = true;
            eventHubTextBox.Text += "Cancelling...\r\n";
            ctsForDataMonitoring.Cancel();
        }

        private static void WriteTextLog(string Log, string DeviceID)
        {
            string folderpath = GetPropertiesString("LoggingPath") + DateTime.Now.ToShortDateString();

            try
            {
                if (folderpath == "") return;

                string filepath = folderpath;

                if (!Directory.Exists(filepath))
                    Directory.CreateDirectory(filepath);

                filepath += "\\[" + DeviceID + "].txt";

                StreamWriter output = new StreamWriter(filepath, true, System.Text.Encoding.Default);
                output.WriteLine(Log);
                output.WriteLine();
                output.Close();
                output = null;
            }
            catch (Exception) { }
        }

        private void LogFolderOpenButton_Click(object sender, EventArgs e)
        {
            string folderpath = GetPropertiesString("LoggingPath");
            if (Directory.Exists(folderpath))
                System.Diagnostics.Process.Start(folderpath);


        }

        private void CescoDeviceExplorer_Load(object sender, EventArgs e)
        {
            cancelMonitoringButton.Enabled = false;
            dataMonitorButton.Enabled = true;
        }

        private async void DeviceMessageCountButton_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(deviceIDsComboBoxForCloudToDeviceMessage.Text))
            {
                MessageBox.Show("ID를 선택하여 주십시오.", "확인", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            MessageCountTextBox.Text = await GetDeviceMessageCount(deviceIDsComboBoxForCloudToDeviceMessage.Text);
        }

        public Task<string> GetDeviceMessageCount(string DeviceID)
        {
            return GetDeviceMessageCountAsync(DeviceID);
        }

        private static async Task<string> GetDeviceMessageCountAsync(string DeviceID)
        {

            Device device = await registryManager.GetDeviceAsync(DeviceID);
            if (device != null)
                return device.CloudToDeviceMessageCount.ToString();
            else
                return "오류";
        }
        private async void checkBoxMonitorFeedbackEndpoint_CheckStateChanged(object sender, EventArgs e)
        {
            if (checkBoxMonitorFeedbackEndpoint.CheckState == CheckState.Checked)
            {
                await StartMonitoringFeedback();
            }
            else
            {
                StopMonitoringFeedback();
            }

        }
        private async Task StartMonitoringFeedback()
        {
            StopMonitoringFeedback();

            ctsForFeedbackMonitoring = new CancellationTokenSource();

            messagesTextBox.Text += $"Started monitoring feedback for device {deviceIDsComboBoxForCloudToDeviceMessage.Text}.\r\n";
            messagesTextBox.SelectionStart = Int32.MaxValue;
            messagesTextBox.ScrollToCaret();

            await MonitorFeedback(ctsForFeedbackMonitoring.Token, deviceIDsComboBoxForCloudToDeviceMessage.Text);
        }
        private async Task MonitorFeedback(CancellationToken ct, string deviceId)
        {
            ServiceClient serviceClient = null;

            serviceClient = ServiceClient.CreateFromConnectionString(IOTHubConnectionString);
            var feedbackReceiver = serviceClient.GetFeedbackReceiver();

            while ((checkBoxMonitorFeedbackEndpoint.CheckState == CheckState.Checked) && (!ct.IsCancellationRequested))
            {
                var feedbackBatch = await feedbackReceiver.ReceiveAsync(TimeSpan.FromSeconds(0.5));
                if (feedbackBatch != null)
                {
                    foreach (var feedbackMessage in feedbackBatch.Records.Where(fm => fm.DeviceId == deviceId))
                    {
                        if (feedbackMessage != null)
                        {
                            messagesTextBox.Text += $"Message Feedback status: \"{feedbackMessage.StatusCode}\", Description: \"{feedbackMessage.Description}\", Original Message Id: {feedbackMessage.OriginalMessageId}\r\n";
                            messagesTextBox.SelectionStart = Int32.MaxValue;
                            messagesTextBox.ScrollToCaret();
                        }
                    }

                    await feedbackReceiver.CompleteAsync(feedbackBatch);
                }
            }

            if (serviceClient != null)
            {
                await serviceClient.CloseAsync();
            }
        }
        void StopMonitoringFeedback()
        {
            if (ctsForFeedbackMonitoring != null)
            {
                messagesTextBox.Text += "Stopped monitoring feedback.\r\n";
                messagesTextBox.SelectionStart = Int32.MaxValue;
                messagesTextBox.ScrollToCaret();
                ctsForFeedbackMonitoring.Cancel();
                ctsForFeedbackMonitoring = null;
            }
        }

        private async void sendMessageToDeviceButton_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(deviceIDsComboBoxForCloudToDeviceMessage.Text))
            {
                MessageBox.Show("ID를 선택하여 주십시오.", "확인", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            try
            {
                string selectedDevice = deviceIDsComboBoxForCloudToDeviceMessage.Text.Trim();
                var SendJson = string.Empty;
                if (tabPane1.SelectedPage == tabNavigationPage5)
                {
                    if (checkBoxSendType.CheckState == CheckState.Checked)
                        SendJson = "{\"MachineID\": \"" + selectedDevice + "\",\"Type\": \"" + TypeComboBox.Text + "\",\"Model\": \"" + ModelTextBox.Text.Trim() + "\",\"CommandCode\": \"" + CommandCodeTextBox1.Text + "\",\"CommandData\": " + CommandDataTextBox1.Text + ",\"SendType\": \"" + SendTypeTextBox.Text.Trim() + "\"}";
                    else
                        SendJson = "{\"MachineID\": \"" + selectedDevice + "\",\"Type\": \"" + TypeComboBox.Text + "\",\"Model\": \"" + ModelTextBox.Text.Trim() + "\",\"CommandCode\": \"" + CommandCodeTextBox1.Text + "\",\"CommandData\": " + CommandDataTextBox1.Text + "}";
                }
                else
                {
                    SendJson = "{\"CommandCode\": \"" + CommandCodeTextBox2.Text + "\",\"CommandData\": " + CommandDataTextBox2.Text + ",\"Utc\": \"" + DateTime.UtcNow + "\"}";
                }

                if (checkBoxAddTimeStamp.Checked)
                    SendJson = DateTime.Now.ToLocalTime() + " - " + SendJson;

                ServiceClient serviceClient = ServiceClient.CreateFromConnectionString(IOTHubConnectionString);
                var serviceMessage = new Microsoft.Azure.Devices.Message(Encoding.UTF8.GetBytes(SendJson));
                serviceMessage.Ack = DeliveryAcknowledgement.Full;
                serviceMessage.MessageId = Guid.NewGuid().ToString();
                await serviceClient.SendAsync(selectedDevice, serviceMessage);
                messagesTextBox.Text += $"Sent to Device ID: [{selectedDevice}], Message:\"{SendJson}\", message Id: {serviceMessage.MessageId}\r\n";
                messagesTextBox.SelectionStart = Int32.MaxValue;
                messagesTextBox.ScrollToCaret();
                await serviceClient.CloseAsync();

            }
            catch (Exception ex)
            {
                using (new CenterDialog(this))
                {
                    MessageBox.Show(ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void checkBoxSendType_CheckStateChanged(object sender, EventArgs e)
        {
            if (checkBoxSendType.CheckState == CheckState.Checked)
            {
                SendTypeTextBox.Enabled = true;
            }
            else
            {
                SendTypeTextBox.Enabled = false;
                SendTypeTextBox.Text = string.Empty;
            }
        }

        private void messageClearButton_Click(object sender, EventArgs e)
        {
            messagesTextBox.Text = string.Empty;
        }

        private async void deviceIDsComboBoxForCloudToDeviceMessage_EditValueChanged(object sender, EventArgs e)
        {
            if (checkBoxMonitorFeedbackEndpoint.CheckState == CheckState.Checked)
            {
                StopMonitoringFeedback();
                await StartMonitoringFeedback();
            }  
        }

        private void clearDataButton_Click_1(object sender, EventArgs e)
        {
            eventHubTextBox.Text = string.Empty;
        }

        private void dockPanel5_Click(object sender, EventArgs e)
        {

        }
    }
}
