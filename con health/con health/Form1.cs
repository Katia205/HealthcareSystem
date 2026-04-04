using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using MQTTnet;
using MQTTnet.Client;
using Newtonsoft.Json;
using MQTTnet.Server;
using System.Threading;
using System.IO.Ports;

namespace con_health
{
    public partial class Form1 : Form
    {
        private IMqttClient mqttClient;
        private List<double> bpmList = new List<double>();
        private List<double> spo2List = new List<double>();
        private int sampleCount = 0;

        public Form1()
        {
            InitializeComponent();
            this.FormClosed += (sender, e) => {
                Application.Exit();
            };
        }

        private async void Form1_Load(object sender, EventArgs e)
        {
            LoadUserData();

            var mqttFactory = new MqttFactory();
            mqttClient = mqttFactory.CreateMqttClient();

            var mqttClientOptions = new MqttClientOptionsBuilder()
                .WithTcpServer("broker.emqx.io", 1883)
                .WithClientId("WinForm_Health_" + Guid.NewGuid().ToString().Substring(0, 5))
                .WithCleanSession()
                .Build();

            try
            {
                string imagePath = Application.StartupPath + @"\canh_bao.png";
                if (System.IO.File.Exists(imagePath))
                    picWarning.Image = Image.FromFile(imagePath);
            }
            catch (Exception ex) { Console.WriteLine("Lỗi load ảnh: " + ex.Message); }

            // --- PHẦN SỬA ĐỔI ĐỂ NHẬN TOPIC GỘP ---
            mqttClient.ApplicationMessageReceivedAsync += ev =>
            {
                var topic = ev.ApplicationMessage.Topic;
                var payload = Encoding.UTF8.GetString(ev.ApplicationMessage.PayloadSegment.ToArray());

                this.Invoke((MethodInvoker)delegate {
                    if (topic == "smarthealth/patient/monitor")
                    {
                        // 1. Xử lý hiển thị BPM/SpO2 từ JSON gộp
                        ProcessData(payload);

                        // 2. Kiểm tra trạng thái ngã trực tiếp từ Payload
                        if (payload.Contains("FALL DETECTED"))
                        {
                            TriggerFallWarning();
                        }
                    }
                });

                return Task.CompletedTask;
            };

            try
            {
                var result = await mqttClient.ConnectAsync(mqttClientOptions, CancellationToken.None);

                if (result.ResultCode == MqttClientConnectResultCode.Success)
                {
                    // Chỉ Subscribe duy nhất topic monitor
                    var mqttSubscribeOptions = new MqttClientSubscribeOptionsBuilder()
                        .WithTopicFilter(f => f.WithTopic("smarthealth/patient/monitor"))
                        .Build();

                    await mqttClient.SubscribeAsync(mqttSubscribeOptions, CancellationToken.None);

                    this.Text = "Hệ thống Sức khỏe - Home";
                }
                else
                {
                    MessageBox.Show("Không thể kết nối MQTT Broker: " + result.ResultCode);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Lỗi khi khởi động MQTT: " + ex.Message);
            }
        }

        private void TriggerFallWarning()
        {
            // Tránh việc hiện nhiều MessageBox nếu nhận tin nhắn liên tục
            if (timerWarning.Enabled) return;

            picWarning.Visible = true;
            timerWarning.Start();
            System.Media.SystemSounds.Exclamation.Play();

            // Lấy dữ liệu hiện tại để lưu vào mốc bị ngã
            double currentBPM = 0;
            double currentSpO2 = 0;
            double.TryParse(lblBPM.Text, out currentBPM);
            double.TryParse(lblSpO2.Text.Replace("%", ""), out currentSpO2);

            SaveToDatabase(currentBPM, currentSpO2, "FALL DETECTED");

            MessageBox.Show("PHÁT HIỆN NGÃ ĐỘT NGỘT! \n\nVui lòng kiểm tra tình trạng người dùng ngay lập tức.",
                            "CẢNH BÁO KHẨN CẤP",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Error);

            picWarning.Visible = false;
            timerWarning.Stop();
        }

        private void timerWarning_Tick(object sender, EventArgs e)
        {
            picWarning.Visible = !picWarning.Visible;
        }

        private void ProcessData(string json)
        {
            try
            {
                dynamic data = JsonConvert.DeserializeObject(json);
                double currentBPM = (double)data.bpm;
                double currentSpO2 = (double)data.spo2;

                // 1. Hiển thị lên giao diện 
                lblBPM.Text = currentBPM.ToString();
                lblSpO2.Text = currentSpO2.ToString() + "%";
                chart1.Series[0].Points.AddY(currentBPM);
                chart2.Series[0].Points.AddY(currentSpO2);
                if (chart1.Series[0].Points.Count > 50) chart1.Series[0].Points.RemoveAt(0);
                if (chart2.Series[0].Points.Count > 50) chart2.Series[0].Points.RemoveAt(0);

                // 2. Logic tính trung bình để lưu SQL
                if (currentBPM > 30 && currentBPM < 200)
                {
                    bpmList.Add(currentBPM);
                    spo2List.Add(currentSpO2);
                    sampleCount++;

                    if (sampleCount >= 5)
                    {
                        double avgBPM = Math.Round(bpmList.Average(), 2);
                        double avgSpO2 = Math.Round(spo2List.Average(), 2);

                        SaveToDatabase(avgBPM, avgSpO2, "Normal");

                        bpmList.Clear();
                        spo2List.Clear();
                        sampleCount = 0;
                    }
                }
            }
            catch (Exception ex) { }
        }

        private void SaveToDatabase(double bpm, double spo2, string status)
        {
            using (var conn = Database.GetConnection())
            {
                try
                {
                    conn.Open();
                    string sql = "INSERT INTO Record (ID_User, BPM_Avg, SpO2_Avg, Record_Time, Fall_status) VALUES (@uid, @bpm, @spo2, @time, @status)";

                    using (var cmd = new System.Data.SqlClient.SqlCommand(sql, conn))
                    {
                        cmd.Parameters.AddWithValue("@uid", Database.CurrentUserID);
                        cmd.Parameters.AddWithValue("@bpm", bpm);
                        cmd.Parameters.AddWithValue("@spo2", spo2);
                        cmd.Parameters.AddWithValue("@time", DateTime.Now);
                        cmd.Parameters.AddWithValue("@status", status);

                        cmd.ExecuteNonQuery();
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine("Lỗi lưu SQL: " + ex.Message);
                }
            }
        }

        private void LoadUserData()
        {
            using (var conn = Database.GetConnection())
            {
                try
                {
                    conn.Open();
                    string sql = "SELECT * FROM [User] WHERE ID = @uid";
                    using (var cmd = new System.Data.SqlClient.SqlCommand(sql, conn))
                    {
                        cmd.Parameters.AddWithValue("@uid", Database.CurrentUserID);
                        using (var reader = cmd.ExecuteReader())
                        {
                            if (reader.Read())
                            {
                                lblUserName.Text = reader["Name"].ToString();
                                if (reader["Birthday"] != DBNull.Value)
                                {
                                    DateTime birthDate = Convert.ToDateTime(reader["Birthday"]);
                                    lblUserBirth.Text = birthDate.ToString("dd/MM/yyyy");
                                }
                                lblUserHeight.Text = reader["Height"].ToString();
                                lblUserWeight.Text = reader["Weight"].ToString();
                            }
                        }
                    }
                }
                catch (Exception ex) { MessageBox.Show("Lỗi: " + ex.Message); }
            }
        }

        private void btnHistory_Click(object sender, EventArgs e)
        {
            FormHistory frm = new FormHistory();
            frm.ShowDialog();
        }

        private void btnLogout_Click(object sender, EventArgs e)
        {
            DialogResult result = MessageBox.Show("Bạn có chắc chắn muốn đăng xuất không?", "Xác nhận", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

            if (result == DialogResult.Yes)
            {
                if (mqttClient != null && mqttClient.IsConnected)
                {
                    mqttClient.DisconnectAsync();
                }
                this.Hide();
                FormLogin login = new FormLogin();
                login.Show();
                login.FormClosed += (s, args) => Application.Exit();
            }
        }
    }
}