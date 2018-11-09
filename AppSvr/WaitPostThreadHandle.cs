using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Net;
using System.IO;
using Newtonsoft.Json;
using System.Collections;

namespace AppSvr
{
    public class WaitPostThreadHandle
    {
        public Thread WaitPostThread;    //等待调用前置接口Post结果返回

        public Thread UdpClientThread;

        HttpListener listerner = new HttpListener();

        public System.Windows.Forms.ContainerControl m_sender = null;
        public Delegate ShowMsgEvent = null;

        public Hashtable TcpClientList = new Hashtable();

        public WaitPostThreadHandle()
        {

        }

        public void Start()
        {
            WaitPostThread = new Thread(new ThreadStart(WaitPostThreadFun));
            WaitPostThread.Name = "WaitPostThread";
            WaitPostThread.Priority = ThreadPriority.Normal;
            WaitPostThread.IsBackground = true;
            WaitPostThread.Start();

            UdpClientThread = new System.Threading.Thread(new System.Threading.ThreadStart(MaintainTcpConnect));
            UdpClientThread.Name = "MaintainTcpConnect";
            UdpClientThread.Start();
        }

        private void MaintainTcpConnect()
        {
            while (true)
            {
                DataRecvProc();
                UdpClientThread.Join(100);
            }
        }

        public void DataRecvProc()
        {
            //循环设备链表
            try
            {
                foreach (DictionaryEntry de in TcpClientList)
                {
                    try
                    {
                        ((DevTcpClient)(de.Value)).ExplainData();
                    }
                    catch (Exception e)
                    {
                        e.GetHashCode();
                        break;
                    }
                }
            }
            catch
            {

            }
        }

        public void Abort()
        {
            try
            {
                if (WaitPostThread != null)
                {
                    try
                    {
                        listerner.Stop();
                        WaitPostThread.Abort();
                        WaitPostThread = null;

                        WriteInfo("WaitPostThread abort successfully!");
                    }
                    catch
                    {
                    }
                }
            }
            catch
            {
            }
        }

        public void WriteInfo(string message)
        {
            message += "\r\n";
            if (m_sender != null) m_sender.BeginInvoke(ShowMsgEvent, new object[] { msgType.Info, message });
        }

        private void WaitPostThreadFun()
        {
            listerner = new HttpListener();

            //循环链表
            while (true)
            {
                string strLocalHttpSvrIPPort = GlbData.LocalHttpSvrIPPort;
                try
                {
                    try
                    {
                        listerner.AuthenticationSchemes = AuthenticationSchemes.Anonymous;//指定身份验证 Anonymous匿名访问

                        listerner.Prefixes.Add("http://" + strLocalHttpSvrIPPort + "/pushserver/");
                        listerner.Start();

                        WriteInfo("Http service for 'http://" + strLocalHttpSvrIPPort + "/pushserver/' start successfully! ");
                    }
                    catch
                    {
                        WriteInfo("Http service for 'http://" + strLocalHttpSvrIPPort + "/pushserver/' start failed! ");
                        break;
                    }

                    //线程池
                    int minThreadNum;
                    int portThreadNum;
                    int maxThreadNum;
                    ThreadPool.GetMaxThreads(out maxThreadNum, out portThreadNum);
                    ThreadPool.GetMinThreads(out minThreadNum, out portThreadNum);
                    while (true)
                    {
                        //等待请求连接
                        //没有请求则GetContext处于阻塞状态
                        HttpListenerContext ctx = listerner.GetContext();

                        ThreadPool.QueueUserWorkItem(new WaitCallback(TaskProc), ctx);
                    }
                }
                catch
                {

                }
                finally
                {
                }
                Thread.Sleep(5);
            }
        }

        void TaskProc(object o)
        {
            HttpListenerContext ctx = (HttpListenerContext)o;

            ctx.Response.StatusCode = 200;//设置返回给客服端http状态代码

            if (ctx.Request.HttpMethod.ToUpper().ToString() == "POST")
            {
                Stream sm;
                StreamReader reader;
                String data;

                try
                {
                    sm = ctx.Request.InputStream;//获取post正文
                    reader = new System.IO.StreamReader(sm, Encoding.UTF8);
                    data = reader.ReadToEnd();
                }
                catch
                {
                    return;
                }

                if (data.ToUpper().Contains("MTRPOINTID") && data.ToUpper().Contains("DATA"))
                {
                    WriteInfo(DateTime.Now.ToString() + ": " + "Receieved data:" + data);
                }
                else
                {

                    WriteInfo(DateTime.Now.ToString() + ": " + "Receieved a post request..");

                    WriteInfo("Post data: " + data);

                    HandlePostData(data, ctx);

                    if (!data.Contains("NBConcenAddr"))
                    {
                        byte[] buffer = System.Text.Encoding.UTF8.GetBytes("00");
                        //对客户端输出相应信息.  
                        try
                        {
                            ctx.Response.ContentLength64 = buffer.Length;
                            System.IO.Stream output = ctx.Response.OutputStream;
                            output.Write(buffer, 0, buffer.Length);             //关闭输出流，释放相应资源            
                            output.Close();
                        }
                        catch
                        { }
                    }
                }
            }
            else
            {
                Stream sm = ctx.Request.InputStream;//获取post正文
                StreamReader reader = new System.IO.StreamReader(sm, Encoding.UTF8);
                String data = reader.ReadToEnd();

                WriteInfo("aaaaaaa data: " + data);
            }
        }

        void HandlePostData(string strData, HttpListenerContext ctx)
        {
            byte[] bFrame = new byte[20];
            int iDataLen = 20;
            string strConcenAddr = "";
            string strDeviceID = "";

            if ((strData.IndexOf("Reportframe") != -1 && strData.IndexOf("deviceDataChanged") != -1) ||

                (strData.ToUpper().IndexOf("RESPONSEFRAME") != -1))
            {
                bool bRestoHttpSvrNBCtrlCmd = false;

                if (strData.IndexOf("Reportframe") != -1)
                {
                    DevReportCmd drc = new DevReportCmd();

                    drc = JsonConvert.DeserializeObject<DevReportCmd>(strData);

                    if (drc.deviceId != "")
                    {
                        if (drc.service.data.Reportframe != null)
                        {
                            string strReportFrame = drc.service.data.Reportframe.Trim();

                            iDataLen = strReportFrame.Length / 2;

                            bFrame = new byte[iDataLen];

                            for (int i = 0; i < iDataLen; i++)
                            {
                                bFrame[i] = DevTcpClient.GetByteFromstrHex(strReportFrame.Substring(2 * i, 2));
                            }
                            strConcenAddr = DevTcpClient.GetStrFromHex(bFrame[8]) + DevTcpClient.GetStrFromHex(bFrame[7]) +
                               DevTcpClient.GetStrFromHex(bFrame[10]) + DevTcpClient.GetStrFromHex(bFrame[9]);

                            if ( (bFrame[0] ==0x68) && (bFrame[1] == 0x10) && ( (bFrame[9] == 0x81) || (bFrame[9] == 0x06) ) )
                            {
                                strConcenAddr = DevTcpClient.GetStrFromHex(bFrame[8]) + DevTcpClient.GetStrFromHex(bFrame[7]) +
                               DevTcpClient.GetStrFromHex(bFrame[6]) + DevTcpClient.GetStrFromHex(bFrame[5]) + DevTcpClient.GetStrFromHex(bFrame[4]) +
                               DevTcpClient.GetStrFromHex(bFrame[3]) + DevTcpClient.GetStrFromHex(bFrame[2]);
                            }

                            strDeviceID = drc.deviceId.Trim();
                        }
                    }
                }
                else if (strData.ToUpper().IndexOf("RESPONSEFRAME") != -1)
                {
                    DevResponse drp = new DevResponse();

                    drp = JsonConvert.DeserializeObject<DevResponse>(strData);

                    if (drp.result.resultDetail.Responseframe != null)
                    {
                        string strResponseFrame = drp.result.resultDetail.Responseframe.Trim();

                        iDataLen = strResponseFrame.Length / 2;

                        bFrame = new byte[iDataLen];

                        for (int i = 0; i < iDataLen; i++)
                        {
                            bFrame[i] = DevTcpClient.GetByteFromstrHex(strResponseFrame.Substring(2 * i, 2));
                        }
                        strConcenAddr = DevTcpClient.GetStrFromHex(bFrame[8]) + DevTcpClient.GetStrFromHex(bFrame[7]) +
                           DevTcpClient.GetStrFromHex(bFrame[10]) + DevTcpClient.GetStrFromHex(bFrame[9]);

                        if ((bFrame[0] == 0x68) && (bFrame[1] == 0x10) && ((bFrame[9] == 0x81) || (bFrame[9] == 0x06)))
                        {
                            strConcenAddr = DevTcpClient.GetStrFromHex(bFrame[8]) + DevTcpClient.GetStrFromHex(bFrame[7]) +
                           DevTcpClient.GetStrFromHex(bFrame[6]) + DevTcpClient.GetStrFromHex(bFrame[5]) + DevTcpClient.GetStrFromHex(bFrame[4]) +
                           DevTcpClient.GetStrFromHex(bFrame[3]) + DevTcpClient.GetStrFromHex(bFrame[2]);
                        }

                        strDeviceID = drp.deviceId.Trim();
                        ////////

                        if (GlbData.NBCtrlCmdList.ContainsKey(strConcenAddr))
                        {
                            NBCtrlCmd NCC = (NBCtrlCmd)GlbData.NBCtrlCmdList[strConcenAddr];
                            NCC.strReturnFrame = strResponseFrame;
                            GlbData.NBCtrlCmdList[strConcenAddr] = NCC;

                            bRestoHttpSvrNBCtrlCmd = true;
                        }
                    }
                }

                if (strConcenAddr.Length == 8)
                {
                    DevTcpClient duc;

                    string strIPPort = GlbData.ServerIPPort;

                    if (TcpClientList.ContainsKey(strConcenAddr))
                    {
                        duc = (DevTcpClient)(TcpClientList[strConcenAddr]);

                        if ( DateTime.Now.Subtract(duc.LastRcvTime) > new TimeSpan(0,5,0) )
                        {
                            duc.iLastSendState = 0;
                        }

                        if (duc.iLastSendState == 0)
                        {
                            TcpClientList.Remove(strConcenAddr);
                            duc = new DevTcpClient(strIPPort);
                            duc.deviceID = strDeviceID;
                            duc.OpenDevice();

                            if (!(bFrame.Length == 20 && bFrame[12] == 0x02))
                            {
                                byte[] bFrame1 = new byte[20];
                                bFrame1[0] = 0x68;
                                bFrame1[1] = 0x32;
                                bFrame1[2] = 0x00;
                                bFrame1[3] = 0x32;
                                bFrame1[4] = 0x00;
                                bFrame1[5] = 0x68;
                                bFrame1[6] = 0xC9;
                                bFrame1[7] = DevTcpClient.GetByteFromstrHex(strConcenAddr.Substring(2, 2));
                                bFrame1[8] = DevTcpClient.GetByteFromstrHex(strConcenAddr.Substring(0, 2));
                                bFrame1[9] = DevTcpClient.GetByteFromstrHex(strConcenAddr.Substring(6, 2));
                                bFrame1[10] = DevTcpClient.GetByteFromstrHex(strConcenAddr.Substring(4, 2));
                                bFrame1[11] = 0x00;
                                bFrame1[12] = 0x02;
                                bFrame1[13] = 0x70;
                                bFrame1[14] = 0x00;
                                bFrame1[15] = 0x00;
                                bFrame1[16] = 0x01;
                                bFrame1[17] = 0x00;
                                bFrame1[18] = DevTcpClient.CheckSum(bFrame1, 6, 12);
                                bFrame1[19] = 0x16;

                                duc.WriteDevice(bFrame1, 0, 20);
                            }
                            if (!TcpClientList.ContainsKey(strConcenAddr))
                                TcpClientList.Add(strConcenAddr, duc);
                        }
                        else
                        {
                            if (duc.deviceID.Trim() != strDeviceID.Trim())
                            {
                                duc.deviceID = strDeviceID.Trim();
                                TcpClientList[strConcenAddr] = duc;
                            }
                        }
                    }
                    else
                    {
                        duc = new DevTcpClient(strIPPort);
                        duc.deviceID = strDeviceID;
                        duc.OpenDevice();
                        Thread.Sleep(500);

                        if (!(bFrame.Length == 20 && bFrame[12] == 0x02))
                        {
                            byte[] bFrame1 = new byte[20];
                            bFrame1[0] = 0x68;
                            bFrame1[1] = 0x32;
                            bFrame1[2] = 0x00;
                            bFrame1[3] = 0x32;
                            bFrame1[4] = 0x00;
                            bFrame1[5] = 0x68;
                            bFrame1[6] = 0xC9;
                            bFrame1[7] = DevTcpClient.GetByteFromstrHex(strConcenAddr.Substring(2, 2));
                            bFrame1[8] = DevTcpClient.GetByteFromstrHex(strConcenAddr.Substring(0, 2));
                            bFrame1[9] = DevTcpClient.GetByteFromstrHex(strConcenAddr.Substring(6, 2));
                            bFrame1[10] = DevTcpClient.GetByteFromstrHex(strConcenAddr.Substring(4, 2));
                            bFrame1[11] = 0x00;
                            bFrame1[12] = 0x02;
                            bFrame1[13] = 0x70;
                            bFrame1[14] = 0x00;
                            bFrame1[15] = 0x00;
                            bFrame1[16] = 0x01;
                            bFrame1[17] = 0x00;
                            bFrame1[18] = DevTcpClient.CheckSum(bFrame1, 6, 12);
                            bFrame1[19] = 0x16;

                            duc.WriteDevice(bFrame1, 0, 20);
                        }
                        if (!TcpClientList.ContainsKey(strConcenAddr))
                            TcpClientList.Add(strConcenAddr, duc);
                    }

                    if (!bRestoHttpSvrNBCtrlCmd)
                        duc.WriteDevice(bFrame, 0, iDataLen);
                }
                else if (strConcenAddr.Length ==14)
                {
                    DevTcpClient duc;

                    string strIPPort = GlbData.ServerIPPort;

                    if (TcpClientList.ContainsKey(strConcenAddr))
                    {
                        duc = (DevTcpClient)(TcpClientList[strConcenAddr]);

                        if (duc.iLastSendState == 0)
                        {
                            TcpClientList.Remove(strConcenAddr);
                            duc = new DevTcpClient(strIPPort);
                            duc.deviceID = strDeviceID;
                            duc.OpenDevice();
                            if (!TcpClientList.ContainsKey(strConcenAddr))
                                TcpClientList.Add(strConcenAddr, duc);
                        }
                        else
                        {
                            if (duc.deviceID.Trim() != strDeviceID.Trim())
                            {
                                duc.deviceID = strDeviceID.Trim();
                                TcpClientList[strConcenAddr] = duc;
                            }
                        }
                    }
                    else
                    {
                        duc = new DevTcpClient(strIPPort);
                        duc.deviceID = strDeviceID;
                        duc.OpenDevice();
                        Thread.Sleep(500);
                        if (!TcpClientList.ContainsKey(strConcenAddr))
                            TcpClientList.Add(strConcenAddr, duc);
                    }

                    if (!bRestoHttpSvrNBCtrlCmd)
                        duc.WriteDevice(bFrame, 0, iDataLen);
                }
                else
                {

                }
            }
            ///////////////130协议水表数据上送
            else if (strData.IndexOf("DeliverySchedule") != -1 && strData.IndexOf("deviceDatasChanged") != -1)
            {
                DevReportCmd_Water drc = new DevReportCmd_Water();

                drc = JsonConvert.DeserializeObject<DevReportCmd_Water>(strData);

                if (drc.deviceId != "")
                {
                    int iIndex = -1;
                    if (drc.services.Length >0 )
                    {
                        for (int i=0;i< drc.services.Length;i++)
                        {
                            if (drc.services[i].serviceType == "DeliverySchedule")
                            {
                                iIndex = i;
                                break;
                            }
                        }
                    }

                    if (iIndex < 0) return;

                    if (drc.services[iIndex].data != null)
                    {
                        string strReportFrame = drc.services[iIndex].data.transpond.Trim();

                        iDataLen = strReportFrame.Length / 2;

                        bFrame = new byte[iDataLen];

                        for (int i = 0; i < iDataLen; i++)
                        {
                            bFrame[i] = DevTcpClient.GetByteFromstrHex(strReportFrame.Substring(2 * i, 2));
                        }

                        string strAddr = (bFrame[9] + bFrame[10] * 256).ToString().PadLeft(5,'0');

                        strConcenAddr = DevTcpClient.GetStrFromHex(bFrame[8]) + DevTcpClient.GetStrFromHex(bFrame[7]) + strAddr;

                        strDeviceID = drc.deviceId.Trim();
                    }
                }

                DevTcpClient duc;

                string strIPPort = GlbData.ServerIPPort;

                if (TcpClientList.ContainsKey(strConcenAddr))
                {
                    duc = (DevTcpClient)(TcpClientList[strConcenAddr]);

                    if (DateTime.Now.Subtract(duc.LastSendTime) > new TimeSpan(0,5,0) )
                    {
                        TcpClientList.Remove(strConcenAddr);
                        duc = new DevTcpClient(strIPPort);
                        duc.deviceID = strDeviceID;
                        duc.OpenDevice();
                        Thread.Sleep(500);
                        if (!TcpClientList.ContainsKey(strConcenAddr))
                            TcpClientList.Add(strConcenAddr, duc);
                    }
                    else
                    {
                        if (duc.deviceID.Trim() != strDeviceID.Trim())
                        {
                            duc.deviceID = strDeviceID.Trim();
                            TcpClientList[strConcenAddr] = duc;
                        }
                    }

                    //if (duc.iLastSendState == 0)
                    //{
                    //    TcpClientList.Remove(strConcenAddr);
                    //    duc = new DevTcpClient(strIPPort);
                    //    duc.deviceID = strDeviceID;
                    //    duc.OpenDevice();
                    //    TcpClientList.Add(strConcenAddr, duc);
                    //}
                    //else
                    //{
                    //    if (duc.deviceID.Trim() != strDeviceID.Trim())
                    //    {
                    //        duc.deviceID = strDeviceID.Trim();
                    //        TcpClientList[strConcenAddr] = duc;
                    //    }
                    //}

                    duc.WriteDevice(bFrame, 0, iDataLen);

                }
                else
                {
                    duc = new DevTcpClient(strIPPort);
                    duc.deviceID = strDeviceID;
                    duc.OpenDevice();
                    Thread.Sleep(500);
                    if (!TcpClientList.ContainsKey(strConcenAddr))
                        TcpClientList.Add(strConcenAddr, duc);

                    duc.WriteDevice(bFrame, 0, iDataLen);
                }
                

            }
            else if (strData.IndexOf("NBConcenAddr") != -1)
            {
                string[] strs = strData.Split(',');

                string[] strs1 = strs[0].Split(':');

                string[] strs2 = strs[1].Split(':');

                string strTermAddr = strs1[1];

                string strCmdFrame = strs2[1];

                //主站主动下发
                List<CommandPara> lsCmdPars = new List<CommandPara>();

                CommandPara currCmdPara = new CommandPara();
                currCmdPara.isNum = false;
                currCmdPara.paraName = "CmdFrame";
                currCmdPara.paraValue = strCmdFrame;
                lsCmdPars.Add(currCmdPara);

                currCmdPara = new CommandPara();
                currCmdPara.isNum = true;
                currCmdPara.paraName = "TimeOut";
                currCmdPara.paraValue = "30";
                lsCmdPars.Add(currCmdPara);

                string strCTIOTID = "";

                string strDeviceID1 = GlbData.GetDeviceID(strTermAddr, ref strCTIOTID);

                if (strDeviceID1 == "") return;

                if (strCTIOTID == "") return;

                if (GlbData.CTIOTAppParaList.Contains(strCTIOTID))
                {
                    CTIOT_APP_PARA ctiot_app_para = (CTIOT_APP_PARA)(GlbData.CTIOTAppParaList[strCTIOTID]);

                    if (ctiot_app_para != null)
                    {
                        NASDK currsdk = new NASDK(ctiot_app_para.SVR_IP, ctiot_app_para.SVR_PORT, ctiot_app_para.APP_ID, ctiot_app_para.APP_PWD,
                        ctiot_app_para.CERT_FILE, ctiot_app_para.CERT_PWD);
                        string strToken = ctiot_app_para.TOKEN;
                        DateTime dtLastgetTokenTime;

                        try
                        {
                            dtLastgetTokenTime = DateTime.Parse(ctiot_app_para.LAST_GETTOKEN_TIME);
                        }
                        catch
                        {
                            dtLastgetTokenTime = new DateTime(2000, 1, 1);
                        }

                        DateTime dtNow = DateTime.Now;
                        if (strToken == "" || (dtLastgetTokenTime < dtNow.AddMinutes(-30)))
                        {
                            TokenResult tr = currsdk.getToken();

                            if (tr == null) return;

                            strToken = tr.accessToken.Trim();
                            string strLastgetTokenTime = dtNow.ToString("yyyy-MM-dd HH:mm:ss");

                            //存储
                            string strSQL = "update CT_IOT_APP_PARA set TOKEN='{0}',LAST_GETTOKEN_TIME='{1}' where ID=" + strCTIOTID;
                            strSQL = string.Format(strSQL, strToken, strLastgetTokenTime);

                            if (GlbData.DBConn.ExeSQL(strSQL))
                            {
                                ctiot_app_para.TOKEN = strToken;
                                ctiot_app_para.LAST_GETTOKEN_TIME = strLastgetTokenTime;

                                GlbData.CTIOTAppParaList[strCTIOTID] = ctiot_app_para;
                            }
                        }

                        string result = currsdk.sendCommand(strToken, strDeviceID1, ctiot_app_para.CALLBACKURL, "CmdService", "Cmd_Down", lsCmdPars);
                        NBCtrlCmdSendResult NCCSR = new NBCtrlCmdSendResult();
                        NCCSR.NBConcenAddr = strTermAddr;

                        if (result == null)
                        {
                            NCCSR.SendTime = "";
                        }
                        else
                        {
                            NBCtrlCmd NCC = new NBCtrlCmd();
                            NCC.NBConcenAddr = strTermAddr;
                            NCC.SendTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                            NCC.strReturnFrame = "";

                            if (!GlbData.NBCtrlCmdList.ContainsKey(strTermAddr))
                            {
                                GlbData.NBCtrlCmdList.Add(strTermAddr, NCC);
                            }
                            else
                            {
                                GlbData.NBCtrlCmdList[strTermAddr] = NCC;
                            }

                            TimeSpan outTimeSpan = new TimeSpan(0, 1, 0);   //超时时间60秒

                            NCC = (NBCtrlCmd) GlbData.NBCtrlCmdList[strTermAddr];

                            NCCSR.NBConcenAddr = strTermAddr;
                            NCCSR.SendTime = NCC.SendTime;

                            while (NCC.strReturnFrame == "")
                            {
                                if ( (DateTime.Now - DateTime.Parse(NCC.SendTime)) > outTimeSpan )
                                {
                                    //返回超时
                                    NCCSR.strReturnFrame = "";
                                    break;
                                }
                            }

                            if (NCC.strReturnFrame!= "")
                            {
                                NCCSR.strReturnFrame = NCC.strReturnFrame;
                            }

                        }

                        string strResult = JsonConvert.SerializeObject(NCCSR); //将消息转成jason格式返回

                        GlbData.NBCtrlCmdList.Remove(strTermAddr);

                        byte[] buffer = System.Text.Encoding.UTF8.GetBytes(strResult);
                        //对客户端输出相应信息.  
                        try
                        {
                            ctx.Response.ContentLength64 = buffer.Length;
                            System.IO.Stream output = ctx.Response.OutputStream;
                            output.Write(buffer, 0, buffer.Length);             //关闭输出流，释放相应资源            
                            output.Close();
                        }
                        catch
                        { }
                    }
                }
                else
                    return;

            }
        }
    }


    public class NBCtrlCmd
    {
        public string NBConcenAddr { get; set; }

        public string SendTime { get; set; }

        public string strReturnFrame { get; set; }
    }

    public class NBCtrlCmdSendResult
    {
        public string NBConcenAddr { get; set; }

        public string SendTime { get; set; }

        public string strReturnFrame { get; set; }
    }
    public class DevReportCmd
    {
        public string notifyType { get; set; }

        public string deviceId { get; set; }

        public string gatewayId { get; set; }

        public string requestId { get; set; }

        public DecReportService service { get; set; }
    }

    public class DecReportService
    {
        public string serviceId { get; set; }

        public string serviceType { get; set; }

        public string gatewayId { get; set; }

        public DecReportData data { get; set; }
}

    public class DecReportData
    {
        public string Reportframe { get; set; }
    }

    public class DevReportCmd_Water
    {
        public string notifyType { get; set; }

        public string deviceId { get; set; }

        public string gatewayId { get; set; }

        public string requestId { get; set; }

        public DecReportService_water[] services { get; set; }
    }

    public class DecReportService_water
    {
        public string serviceId { get; set; }

        public string serviceType { get; set; }

        public string gatewayId { get; set; }

        public DecReportData_Water data { get; set; }
    }

    public class DecReportData_Water
    {
        public string transpond { get; set; }
    }

    public class DevResponse
    {
        public string deviceId { get; set; }
        public string commandId { get; set; }

        public DevResponseResult result { get; set; }
    }

    public class DevResponseResult
    {
        public string resultCode { get; set; }

        public DevResponseResultDetail resultDetail { get; set; }
    }

    public class DevResponseResultDetail
    {
        public int result { get; set; }
        public string Responseframe { get; set; }
    }

    
}
