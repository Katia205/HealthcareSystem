using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Data.SqlClient;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace con_health
{
    public partial class FormLogin : Form
    {
        public FormLogin()
        {
            InitializeComponent();

            this.FormClosed += (sender, e) => {
                Application.Exit();
            };
        }

        private void btnLogin_Click(object sender, EventArgs e)
        {
            using (var conn = Database.GetConnection())
            {
                try
                {
                    conn.Open();
                    string inputHash = Database.HashPassword(txtPass.Text); // Mã hóa cái vừa nhập

                    string sql = "SELECT ID FROM [User] WHERE Email=@e AND Password=@p";
                    SqlCommand cmd = new SqlCommand(sql, conn);
                    cmd.Parameters.AddWithValue("@e", txtEmail.Text);
                    cmd.Parameters.AddWithValue("@p", inputHash);

                    object result = cmd.ExecuteScalar();

                    if (result != null && result != DBNull.Value)
                    {
                        // 1. Lưu ID vào biến toàn cục để Form1 sử dụng
                        Database.CurrentUserID = Convert.ToInt32(result);

                        MessageBox.Show("Đăng nhập thành công!");

                        // 2. Mở Form chính
                        Form1 mainForm = new Form1();
                        mainForm.Show();

                        // 3. Ẩn Form đăng nhập
                        this.Hide();
                    }
                    else
                    {
                        MessageBox.Show("Sai tài khoản hoặc mật khẩu!");
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Lỗi kết nối: " + ex.Message);
                }
        }
        }

       

        private void linkRegister_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            FormRegister reg = new FormRegister();
            
            reg.FormClosed += (s, args) => this.Show();

            reg.Show();
            this.Hide();
        }

        
    }
}
