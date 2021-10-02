using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace GrabThatDesktop
{
    public partial class Form1 : Form
    {
        public Form1()
        {
            InitializeComponent();
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            Size = new Size(0, 0);
            Visible = false;

            desktopSelectorStrip.Items.Clear();
            foreach (Screen screen in Screen.AllScreens)
            {
                string display_number = Regex.Match(screen.DeviceName, @"\d+").Value;
                ToolStripMenuItem item = (ToolStripMenuItem) desktopSelectorStrip.Items.Add(display_number);
                if (screen.Primary)
                {
                    item.Checked = true;
                }
            }
        }

        private void Form1_Shown(object sender, EventArgs e)
        {
            Hide();
        }

        private void desktopSelectorStrip_Opening(object sender, CancelEventArgs e)
        {

        }

        private void contextMenuStrip1_ItemClicked(object sender, ToolStripItemClickedEventArgs e)
        {
            if (e.ClickedItem == exitMenuItem)
            {
                System.Windows.Forms.Application.Exit();
            }
        }
    }
}
