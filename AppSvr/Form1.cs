using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Threading;
using System.Collections;

namespace AppSvr
{
    public enum msgType
    {
        Debug = 1,
        Info = 2,
        Warn = 3,
        Error = 4,
        Fatal = 5,
    }
    
    public partial class Form1 : Form
    {

        public delegate void ShowMsgEvent(msgType type, object msg);

        WaitPostThreadHandle wpt;

        public static Hashtable tcCommSvrList = new Hashtable();

        public Form1()
        {
            InitializeComponent();

            GlbData.GetNAInfo();

        }

        private void label2_Click(object sender, EventArgs e)
        {
        }

        private void button7_Click(object sender, EventArgs e)
        {
            try
            {
                if (wpt != null)
                {
                    wpt.Abort();
                }
            }
            catch
            {

            }
            finally
            {
                button6.Enabled = true;
                button7.Enabled = false;
            }
        }

        private void button6_Click(object sender, EventArgs e)
        {
            button6.Enabled = false;
            button7.Enabled = true;

            wpt = new WaitPostThreadHandle();

            wpt.m_sender = this;

            ShowMsgEvent ShowMsgEvent = new ShowMsgEvent(MsgEvenDeal);

            wpt.ShowMsgEvent = ShowMsgEvent;

            try
            {
                if (wpt != null)
                {
                    wpt.Start();
                }
            }
            catch
            {

            }
        }

        private void MsgEvenDeal(msgType type, object msg)
        {
            if (richTextBox1.Lines.Length >= 200)
            {
                richTextBox1.Clear();
            }
            richTextBox1.AppendText(msg.ToString());
        }

        private void richTextBox1_TextChanged(object sender, EventArgs e)
        {

        }

        private void button13_Click(object sender, EventArgs e)
        {
            richTextBox1.Clear();
        }

        public void UpdateParaValue(string strParaName,string strParaValue)
        {
            string strSQL = "update sys_para_config set para_value= '{1}' where para_name= '{0}' ";

            strSQL += " if @@rowcount =0 begin insert into sys_para_config values ('{2}','{3}') end; ";

            strSQL = string.Format(strSQL,strParaName,strParaValue, strParaName, strParaValue);

            GlbData.DBConn.ExeSQL(strSQL);

        }

        private void button3_Click(object sender, EventArgs e)
        {

        }
    }
}
