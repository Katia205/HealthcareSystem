using System;
using System.Data.SqlClient;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace con_health
{
    public partial class FormRegister : Form
    {
        public FormRegister()
        {
            InitializeComponent();
            // Đảm bảo các ô mật khẩu được ẩn ký tự
            txtPass.UseSystemPasswordChar = true;
            txtConfirmPass.UseSystemPasswordChar = true;
        }

        

        public bool ValidateUserInput(string hoTen, DateTime ngaySinh, float chieuCao, float canNặng, string email, string matKhau, out string errorMessage)
        {
            errorMessage = "";

            // 1. Kiểm tra Họ tên
            if (hoTen.Trim().Length < 2)
            {
                errorMessage = "Họ tên phải có ít nhất 2 ký tự.";
                return false;
            }
            if (Regex.IsMatch(hoTen, @"\d"))
            {
                errorMessage = "Họ tên không được chứa chữ số.";
                return false;
            }

            // 2. Ngày sinh phải trước hôm nay (Quá khứ)
            if (ngaySinh.Date >= DateTime.Now.Date)
            {
                errorMessage = "Ngày sinh phải là một ngày trong quá khứ.";
                return false;
            }

            if (ngaySinh.Year < 1920)
            {
                errorMessage = "Ngày sinh không được trước năm 1920.";
                return false;
            }

            // 3. Chiều cao >= 20, Cân nặng >= 1
            if (chieuCao < 20 || canNặng < 1)
            {
                errorMessage = "Chiều cao phải từ 20cm và Cân nặng phải từ 1kg trở lên.";
                return false;
            }

            // 4. Kiểm tra Email (Sử dụng Regex)
            string emailPattern = @"^[^@\s]+@[^@\s]+\.[^@\s]+$";
            if (!Regex.IsMatch(email, emailPattern))
            {
                errorMessage = "Email không đúng định dạng.";
                return false;
            }

            try
            {
                using (SqlConnection conn = Database.GetConnection())
                {
                    conn.Open();
                    string checkSql = "SELECT COUNT(*) FROM [User] WHERE Email = @email";
                    using (SqlCommand cmd = new SqlCommand(checkSql, conn))
                    {
                        cmd.Parameters.AddWithValue("@email", email);
                        int count = (int)cmd.ExecuteScalar();
                        if (count > 0)
                        {
                            errorMessage = "Email này đã được sử dụng. Vui lòng dùng email khác.";
                            return false;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                errorMessage = "Lỗi kết nối database khi kiểm tra email: " + ex.Message;
                return false;
            }

            // 5. Mật khẩu: Tối thiểu 6 ký tự, có chữ Hoa và ký tự Đặc biệt
            // Biểu thức chính quy: 
            // (?=.*[A-Z]) : Ít nhất 1 chữ hoa
            // (?=.*[\W_]) : Ít nhất 1 ký tự đặc biệt
            // .{6,}       : Độ dài tối thiểu 6
            string passwordPattern = @"^(?=.*[A-Z])(?=.*[\W_]).{6,}$";
            if (!Regex.IsMatch(matKhau, passwordPattern))
            {
                errorMessage = "Mật khẩu phải có ít nhất 6 ký tự, bao gồm chữ viết hoa và ký tự đặc biệt.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(txtName.Text) || string.IsNullOrWhiteSpace(txtEmail.Text) || string.IsNullOrWhiteSpace(txtPass.Text))
            {
                MessageBox.Show("Vui lòng nhập đầy đủ các trường bắt buộc!", "Thông báo");
                return false;
            }

            if (txtPass.Text != txtConfirmPass.Text)
            {
                MessageBox.Show("Mật khẩu nhập lại không khớp!", "Lỗi");
                return false;
            }

            return true; // Tất cả đều hợp lệ
        }

        private void btnRegister_Click(object sender, EventArgs e)
        {
            // 1. Lấy và ép kiểu dữ liệu từ giao diện
            string hoTen = txtName.Text;
            DateTime ngaySinh = dtpBirthday.Value;
            string email = txtEmail.Text;
            string matKhau = txtPass.Text;

            // Ép kiểu chiều cao, cân nặng (dùng TryParse để tránh lỗi nếu người dùng nhập chữ)
            float chieuCao, canNang;
            float.TryParse(txtHeight.Text, out chieuCao);
            float.TryParse(txtWeight.Text, out canNang);

            // 2. Gọi hàm Validate
            string error;
            if (!ValidateUserInput(hoTen, ngaySinh, chieuCao, canNang, email, matKhau, out error))
            {
                // Nếu có lỗi, hàm Validate đã hiện MessageBox bên trong hoặc trả ra error
                // Ở đây ta hiện thêm error nếu chuỗi error không rỗng
                if (!string.IsNullOrEmpty(error))
                {
                    MessageBox.Show(error, "Lỗi nhập liệu", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                return; // Dừng lại, không chạy xuống phần lưu SQL
            }

            // 3. Nếu vượt qua Validate, thực hiện lưu vào SQL
            using (SqlConnection conn = Database.GetConnection())
            {
                try
                {
                    conn.Open();

                    // Băm mật khẩu (Dùng hàm băm bạn đã viết trong class Database)
                    string hashed = Database.HashPassword(txtPass.Text);

                    string sql = "INSERT INTO [User] (Name, Birthday, Password, Height, Weight, Email) " +
                                 "VALUES (@name, @birthday, @pass, @h, @w, @email)";

                    using (SqlCommand cmd = new SqlCommand(sql, conn))
                    {
                        cmd.Parameters.AddWithValue("@name", hoTen);
                        cmd.Parameters.AddWithValue("@birthday", ngaySinh.Date);
                        cmd.Parameters.AddWithValue("@pass", hashed);
                        cmd.Parameters.AddWithValue("@h", chieuCao);
                        cmd.Parameters.AddWithValue("@w", canNang);
                        cmd.Parameters.AddWithValue("@email", email);

                        cmd.ExecuteNonQuery();
                        MessageBox.Show("Đăng ký tài khoản thành công!", "Thành công");
                        this.Close();
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Lỗi khi đăng ký: " + ex.Message);
                }
            }
        }

        private void linkLogin_LinkClicked(object sender, EventArgs e)
        {
           this.Close();
        }

       
    }
}