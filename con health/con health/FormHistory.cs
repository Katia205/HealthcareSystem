using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace con_health
{
    public partial class FormHistory : Form
    {
        private Timer refreshTimer;

        public FormHistory()
        {
            InitializeComponent();
            SetupDataGridView();
            LoadHistory();

            refreshTimer = new Timer();
            refreshTimer.Interval = 2000; // 2 giây cập nhật 1 lần
            refreshTimer.Tick += RefreshTimer_Tick;
            refreshTimer.Start();

            // Dừng timer khi đóng Form để tránh tốn tài nguyên
            this.FormClosing += (s, e) => refreshTimer.Stop();
        }

        private void SetupDataGridView()
        {
            dgvData.ReadOnly = true;
            dgvData.AllowUserToAddRows = false;
            dgvData.RowHeadersVisible = false;
            dgvData.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            dgvData.BackgroundColor = Color.White;

            // Thiết lập mặc định ban đầu
            dgvData.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;

            dgvData.DataBindingComplete += DgvData_DataBindingComplete;
        }

        private void LoadHistory()
        {
            using (var conn = Database.GetConnection())
            {
                try
                {
                    conn.Open();

                    string sql = @"
                        SELECT * FROM (
                            SELECT 
                                ROW_NUMBER() OVER (ORDER BY Record_Time ASC) AS STT,
                                Record_Time AS [Thời gian],
                                BPM_Avg AS [Nhịp tim], 
                                SpO2_Avg AS [SpO2 (%)], 
                                Fall_status AS [Trạng thái]
                            FROM Record 
                            WHERE ID_User = @uid
                        ) AS Temp
                        ORDER BY [Thời gian] DESC";

                    System.Data.SqlClient.SqlDataAdapter adapter = new System.Data.SqlClient.SqlDataAdapter(sql, conn);
                    adapter.SelectCommand.Parameters.AddWithValue("@uid", Database.CurrentUserID);

                    DataTable dt = new DataTable();
                    adapter.Fill(dt);

                    dgvData.DataSource = dt;

                    // --- PHẦN CHỈNH CỘT ĐỂ KHÔNG BỊ DẤU ... ---

                    // 1. Cột STT: Cho nhỏ lại vừa đủ
                    dgvData.Columns["STT"].AutoSizeMode = DataGridViewAutoSizeColumnMode.AllCells;
                    dgvData.Columns["STT"].HeaderText = "Lần đo";

                    // 2. Cột Thời gian: Ép hiện hết nội dung
                    dgvData.Columns["Thời gian"].AutoSizeMode = DataGridViewAutoSizeColumnMode.AllCells;
                    // Định dạng hiển thị: dd/MM/yyyy HH:mm:ss (Ví dụ: 25/02/2026 17:30:00)
                    dgvData.Columns["Thời gian"].DefaultCellStyle.Format = "dd/MM/yyyy HH:mm:ss";

                    // 3. Cột Trạng thái: Để cuối và chiếm phần diện tích còn lại
                    dgvData.Columns["Trạng thái"].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Lỗi: " + ex.Message);
                }
            }
        }

        private void DgvData_DataBindingComplete(object sender, DataGridViewBindingCompleteEventArgs e)
        {
            foreach (DataGridViewRow row in dgvData.Rows)
            {
                if (row.Cells["Trạng thái"].Value != null &&
                    row.Cells["Trạng thái"].Value.ToString() == "FALL DETECTED")
                {
                    row.DefaultCellStyle.ForeColor = Color.Red;
                    row.DefaultCellStyle.Font = new Font(dgvData.Font, FontStyle.Bold);
                }
            }
        }

        private void RefreshTimer_Tick(object sender, EventArgs e)
        {
            LoadHistory();
        }
    }
}