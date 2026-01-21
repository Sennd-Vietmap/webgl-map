using System.Windows.Forms;

namespace VectorMap.WinForms
{
    public partial class Form1 : Form
    {
        public Form1()
        {
            InitializeComponent();
            
            this.Text = "Vector Tile Map - WinForms";
            this.Size = new System.Drawing.Size(1024, 768);
        }
    }
}
