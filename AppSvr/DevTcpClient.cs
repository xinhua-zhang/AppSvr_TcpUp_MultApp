using System;
using System.Collections.Generic;
using System.Web;
using System.Configuration;
using System.Windows;
using System.Windows.Forms;
using System.Data;
using System.Net.Sockets;
using System.Net;
using System.Collections;
using System.IO;
using System.Text;
using Newtonsoft.Json;

namespace AppSvr
{
    public class DevTcpClient
    {
        /// <summary> 
        /// 获得与客户端Socket对象 
        /// </summary> 
        public Socket ClientSocket
        {
            get
            {
                return ClientSock;
            }
        }

        /// <summary> 
        /// IP+Port
        /// </summary> 
        private IPEndPoint iep;

        private const int DefaultDevCapacity = 10240;  // 接收缓冲长度   10K

        private const int DefaultSocketCapacity = 1024; // 每次接受报文的最大长度1024 

        private ClientId _id;                           // Client标示ID 

        public int iLastSendState;

        public DateTime LastSendTime;

        public string deviceID;

        /// <summary> 
        /// 服务器发送到客户端的数据
        /// </summary> 
        private byte[] DevRecvBuf;                      //接收缓冲区，设备缓冲区
        private int m_DevRecvIndex;                     //接收索引
        private int m_DevRecvDealIndex;                 //接收处理索引

        /// <summary> 
        /// 每次接受报文数组
        /// </summary> 
        public byte[] SocketDevRecvBuf;//Socket Recive消息接收到的报文

        private bool _HaveReceievedData = false;

        private object mLock = new object(); //锁异步接收数据时，保护接收缓冲

        private object mSendLock = new object();  //锁异步发送数据

        /// <summary>
        /// 最近接收信息时间 用来判断超时 6min
        /// </summary>
        public DateTime LastRcvTime = DateTime.Now;

        /// <summary>
        /// 是否连接成功
        /// </summary>
        private bool Opened;

        /// <summary>
        /// 设备状态
        /// </summary>
        private DeviceStatus DevStatus;
        /// <summary>
        /// 请求切换状态
        /// </summary>
        private DeviceStatus RequestStatus;

        private int count;
        private int retryCount;

        /// <summary> 
        /// 客户端的Socket 
        /// </summary> 
        private Socket ClientSock;

        private string ConcenAddr;

        /// <summary>
        ///  Busy时间 防止socket连接一直处于busy状态，强制将其关闭
        /// </summary>
        private DateTime busytm;

        public object ModifyLock = new object(); //锁异步接收数据时，保护接收缓冲

        public delegate void NetEvent(object sender, DevTcpClient e);

        /// <summary> 
        /// 已经连接服务器事件 
        /// </summary> 
        public event NetEvent ConnectedServer;
        /// <summary> 
        /// 接收到数据报文事件 
        /// </summary> 
        public event NetEvent ReceivedDatagram;
        /// <summary> 
        /// 连接断开事件 
        /// </summary> 
        public event NetEvent DisConnectedServer;

        public DevTcpClient(string strIPPort)
        {
            string[] commInfo = strIPPort.Split(':');
            string hostname = commInfo[0];

            IPAddress ip;

            if (hostname.Contains("."))
            {
                ip = IPAddress.Parse(hostname);
                iep = new IPEndPoint(ip, int.Parse(commInfo[1]));
            }
            else
            {
                IPAddress[] hostEntry = Dns.GetHostEntry(hostname).AddressList;
                for (int i = 0; i < hostEntry.Length; ++i)
                {
                    if (hostEntry[i].AddressFamily == AddressFamily.InterNetwork)
                    {
                        ip = hostEntry[i];
                        iep = new IPEndPoint(ip, int.Parse(commInfo[1]));
                        break;
                    }
                }
            }

            DevRecvBuf = new byte[DefaultDevCapacity];
            m_DevRecvIndex = 0;
            m_DevRecvDealIndex = 0;

            SocketDevRecvBuf = new byte[DefaultSocketCapacity];

            Opened = false;

            DevStatus = DeviceStatus.Close;
            RequestStatus = DeviceStatus.Opened;

            count = 0;
            retryCount = 3;

            _HaveReceievedData = false;
        }

        /// <summary>
        /// 维护 客户端与服务器端失去联系 尝试重连
        /// </summary>
        public void MaintainDevice()
        {
            if (DevStatus == DeviceStatus.Ready)
            {
                if ((this._HaveReceievedData) && ((DateTime.Now - this.LastRcvTime) > TimeSpan.FromMinutes(5)))
                {
                    if (ClientSock != null)
                    {
                        byte[] buffer = new byte[13];

                        string strConcenAddr = ConcenAddr.PadLeft(8, '0').Substring(0, 8);

                        buffer[0] = 0x66;
                        buffer[1] = 0x08;
                        buffer[2] = 0x66;
                        buffer[3] = 0x00;
                        buffer[4] = 0x04;
                        buffer[5] = 0x00;
                        buffer[6] = GetByteFromstrHex(strConcenAddr.Substring(2, 2));
                        buffer[7] = GetByteFromstrHex(strConcenAddr.Substring(0, 2));
                        buffer[8] = GetByteFromstrHex(strConcenAddr.Substring(6, 2));
                        buffer[9] = GetByteFromstrHex(strConcenAddr.Substring(4, 2));
                        buffer[10] = CheckSum(buffer, 6, 4);
                        buffer[11] = CheckSum(buffer, 3, 8);
                        buffer[12] = 0x16;

                        ClientSock.BeginSend(buffer, 0, 13, SocketFlags.None, new AsyncCallback(SendDataEnd), ClientSock);
                        this.LastRcvTime = DateTime.Now;
                    }
                }
                return;
            }
            //////////////////

            if (DevStatus == DeviceStatus.Malfunction)
            {
                count++;
                if (count >= retryCount)
                {
                    CloseDevice();
                    RequestStatus = DeviceStatus.Opened;
                    DevStatus = DeviceStatus.Close;
                    count = 0;
                }
                return;
            }

            if (RequestStatus == DeviceStatus.Close || RequestStatus == DeviceStatus.ManualClose)
            {
                RequestStatus = DeviceStatus.Opened;
            }

            ////////////////

            switch (RequestStatus)
            {
                case DeviceStatus.Opened:
                case DeviceStatus.Ready:

                    if (DevStatus == DeviceStatus.Close)
                    {
                        if (count >= retryCount)
                        {
                            //三次连接不成功 放弃连接
                            //CloseDevice(true);
                            DevStatus = DeviceStatus.Malfunction; //置设备状态为损坏标志
                            count = 0;
                            return;
                        }

                        OpenDevice();

                        if (Opened)
                        {
                            count = 0;
                            DevStatus = DeviceStatus.Ready;
                        }
                        count++;

                        return;
                    }

                    if (DevStatus == DeviceStatus.Busy && (DateTime.Now - busytm) >= TimeSpan.FromMinutes(3))
                    {
                        //GlbData.CommErrLogToFile(string.Format("Long Time Busy! Close! Try Again---ID:{0}--Count:{1}", ID, count));
                        CloseDevice();
                        return;
                    }
                    return;

                case DeviceStatus.Close:

                    CloseDevice();
                    return;
                default:
                    break;
            }
        }

        public void CloseDevice()
        {
            try
            {
                lock (ModifyLock)
                {
                    if (ClientSock == null)
                    {
                        Opened = false;
                        DevStatus = DeviceStatus.Close;
                        return;
                    }

                    //if (Opened)
                    //    //关闭数据的接受和发送 
                    try
                    {
                        ClientSock.Shutdown(SocketShutdown.Both);
                    }
                    catch
                    { }

                    //清理资源 
                    ClientSock.Close();

                    //Added by zhangxh at 2013.06.07
                    ((IDisposable)(ClientSock)).Dispose();

                    ClientSock = null;
                    DevStatus = DeviceStatus.Close;
                    Opened = false;
                }
            }
            catch (SocketException e)
            {
            }
        }

        /// <summary>
        /// 打开设备 连接服务器端
        /// </summary>
        /// <returns></returns>
        public bool OpenDevice()
        {
            if (Opened)
                return true;

            try
            {
                lock (ModifyLock)
                {
                    if (iep == null)
                        return false;

                    ClientSock = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

                    //Added by zhangxh at 2014.09.08,使避免进入Time_Wait状态
                    try
                    {
                        ClientSock.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Linger, new LingerOption(false, 0));
                    }
                    catch
                    {
                    }

                    DevStatus = DeviceStatus.Busy;
                    busytm = DateTime.Now;

                    //////////////Added by zhangxh at 2010.06.08
                    //请注意这一句。ReuseAddress选项设置为True将允许将套接字绑定到已在使用中的地址。 
                    //ClientSock.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                    //ClientSock.Bind(iep);
                    //ClientSock.Listen(100); 
                    ///////////

                    ClientSock.BeginConnect(iep, new AsyncCallback(Connected), ClientSock);

                    _id = new ClientId((int)ClientSock.Handle);

                    m_DevRecvIndex = 0;
                    m_DevRecvDealIndex = 0;
                }
                return true;
            }
            catch (Exception e)
            {

                DevStatus = DeviceStatus.Close;
                Opened = false;

                return false;
            }
        }

        protected void Connected(IAsyncResult iar)
        {
            Socket client = null;
            try
            {
                lock (ModifyLock)
                {

                    if (DevStatus != DeviceStatus.Busy)
                        return;

                    client = (Socket)iar.AsyncState;


                    //Added by zhangxh at 2014.09.08,使避免进入Time_Wait状态
                    try
                    {
                        client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Linger, new LingerOption(false, 0));
                    }
                    catch
                    {
                    }

                    if (client == null)
                    {
                        DevStatus = DeviceStatus.Close;
                        return;
                    }
                    client.EndConnect(iar);

                    Opened = true;
                    count = 0;
                    DevStatus = DeviceStatus.Ready;

                    ///////////

                    //触发连接建立事件 
                    if (ConnectedServer != null)
                    {
                        ConnectedServer(this, this);
                    }

                    //if (ClientSock != null)
                    //{
                    //    byte[] buffer = new byte[13];

                    //    string strConcenAddr = ConcenAddr.PadLeft(8, '0').Substring(0, 8);

                    //    buffer[0] = 0x66;
                    //    buffer[1] = 0x08;
                    //    buffer[2] = 0x66;
                    //    buffer[3] = 0x00;
                    //    buffer[4] = 0x04;
                    //    buffer[5] = 0x00;
                    //    buffer[6] = GetByteFromstrHex(strConcenAddr.Substring(2, 2));
                    //    buffer[7] = GetByteFromstrHex(strConcenAddr.Substring(0, 2));
                    //    buffer[8] = GetByteFromstrHex(strConcenAddr.Substring(6, 2));
                    //    buffer[9] = GetByteFromstrHex(strConcenAddr.Substring(4, 2));
                    //    buffer[10] = CheckSum(buffer, 6, 4);
                    //    buffer[11] = CheckSum(buffer, 3, 8);
                    //    buffer[12] = 0x16;

                    //    ClientSock.BeginSend(buffer, 0, 13, SocketFlags.None, new AsyncCallback(SendDataEnd), ClientSock);
                    //    this.LastRcvTime = DateTime.Now;
                    //}

                    if (client != null)
                    {
                        //建立连接后应该立即接收数据 
                        client.BeginReceive(SocketDevRecvBuf, 0, DefaultSocketCapacity, SocketFlags.None,
                                                new AsyncCallback(RecvData), client);

                        //////////// Added by zhangxh at 2010.06.10
                        if (client.Connected)
                        {
                            //Logger.WriteHint(ResString.ComDev0035 + " " + this.IPPort.Address.ToString() +
                            //    ":" + this.IPPort.Port.ToString() + " " + ResString.ComDev0034);
                        }

                    }
                }
            }
            catch (SocketException sEx)
            {
                /////////Added by zhangxh at 2010.06.10
                //"由于套接字没有连接并且(当使用一个 sendto 调用发送数据报套接字时)没有提供地址，发送或接收数据的请求没有被接受。"
                //if (sEx.ErrorCode != 10057)
                //    Debug.WriteLine(sEx.TargetSite + " : " + sEx.Message);
                DevStatus = DeviceStatus.Close;
                Opened = false;
                ////////

            }
            catch (Exception ex)
            {

                //Debug.WriteLine(ex.TargetSite + " : " + ex.Message);
                CloseDevice();
            }
        }

        /// <summary> 
        /// 数据接收处理函数 
        /// 异步处理函数，不受控！！！
        /// 此处前置作为客户端接收数据，SocketClient不在ClientList链表中，ClearGarbage不会删除此连接。
        /// </summary> 
        /// <param name="iar">异步Socket</param> 
        protected void RecvData(IAsyncResult iar)
        {
            this.LastRcvTime = DateTime.Now;
            ////////////////////////

            Socket remote = (Socket)iar.AsyncState;
            int recv = 0;
            try
            {
                if (remote == null)
                {
                    DevStatus = DeviceStatus.Close;
                    CloseDevice();
                    return;
                }
                recv = remote.EndReceive(iar);                  //返回值接收到的字节数

                //////////////// 将原处理项进行合并,Modified by zhangxh at 20090629 ///////////
                //正常的退出 
                //////////**** Marked by zhangxh at 2010.0,可能存在发空帧的现象，
                if (recv == 0)
                {
                    DevStatus = DeviceStatus.Close;
                    CloseDevice();
                    return;
                }

                SaveRcv(SocketDevRecvBuf, 0, recv);

                //通过事件发布收到的报文 
                //if (ReceivedDatagram != null)
                //    ReceivedDatagram(this, new DevTcpClient(remote, ServerSock));

                if (DevStatus == DeviceStatus.Ready || DevStatus == DeviceStatus.Opened)
                {//继续接收来自来客户端的数据 
                    ClientSock.BeginReceive(SocketDevRecvBuf, 0, DefaultSocketCapacity, SocketFlags.None,
                                            new AsyncCallback(RecvData), ClientSock);
                }
            }
            catch (SocketException ex)
            {
                //客户端退出 
                if (ex.ErrorCode == 10054)
                {
                    //服务器强制的关闭连接，强制退出 

                    //GlbData.CommErrLogToFile(ResString.ComDev0032 + " " + this.IPPort.Address.ToString() +
                    //    ":" + this.IPPort.Port.ToString() + " " + ResString.ComDev0033);

                    //TypeExit = ExitType.ExceptionExit;

                    //if (DisConnectedServer != null)
                    //    DisConnectedServer(this, new DevTcpClient(remote, ServerSock));

                    DevStatus = DeviceStatus.Close;
                    CloseDevice();
                }
                else
                {
                    throw (ex);
                }
            }
            catch (ObjectDisposedException ode)
            {
                //GlbData.CommErrLogToFile(ode.TargetSite + " : " + ode.Message);
            }
            catch (Exception e)
            {
                //GlbData.CommErrLogToFile(e.TargetSite + " : " + e.Message);
                //客户端强制关闭 
                DevStatus = DeviceStatus.Close;
                CloseDevice();

                return;
            }
        }

        /// <summary>
        /// 保存接收来的数据
        /// </summary>
        /// <param name="data"></param>
        /// <param name="offset"></param>
        /// <param name="count"></param>
        public void SaveRcv(byte[] data, int offset, int count)
        {
            lock (mLock)//
            {
                for (int i = 0; i < count; i++)
                {
                    DevRecvBuf[m_DevRecvIndex] = data[offset + i];
                    m_DevRecvIndex++;
                    m_DevRecvIndex = m_DevRecvIndex % DefaultDevCapacity;
                }
            }
            LastRcvTime = DateTime.Now;        //标志接收到数据

            //Added by zhangxh at 2013.06.06
            if (count > 0)
            {
                this.LastRcvTime = DateTime.Now;
                this._HaveReceievedData = true;
            }
        }


        public void ExplainData()
        {
            if (m_DevRecvIndex > m_DevRecvDealIndex) //表示有接收到数据
            {
                //检出有效帧
                while (((m_DevRecvIndex + DefaultDevCapacity - m_DevRecvDealIndex) % DefaultDevCapacity) > 0)
                {
                    //帧头判断
                    if (DevRecvBuf[(m_DevRecvDealIndex + 0) % DefaultDevCapacity] != 0x68)
                    {
                        m_DevRecvDealIndex = (m_DevRecvDealIndex + 1) % DefaultDevCapacity; //检测不到帧头，非正常数据，丢弃本字节
                        continue;                                   //继续寻找帧头
                    }

                    if (DevRecvBuf[(m_DevRecvDealIndex + 1) % DefaultDevCapacity] == 0x10)
                    {
                        int iPickResult = PickOut188Frame();
                        if (iPickResult == 0) return;
                    }

                    int recvDataLen = (m_DevRecvIndex + DevRecvBuf.Length - m_DevRecvDealIndex) % DevRecvBuf.Length;

                    //小于帧基本长度，继续接收
                    if (recvDataLen < 14)
                    {
                        return;
                    }
                    //第2个68判断
                    if (DevRecvBuf[(m_DevRecvDealIndex + 5) % DefaultDevCapacity] != 0x68)
                    {
                        m_DevRecvDealIndex = (m_DevRecvDealIndex + 1) % DefaultDevCapacity;
                        continue;                                   //继续寻找帧头
                    }

                    //数据区长度
                    int dataLen1 = 0, dataLen2 = 0;

                    dataLen1 = ((DevRecvBuf[(m_DevRecvDealIndex + 1) % DevRecvBuf.Length] & 0xFC) / 4
                              + 0x40 * (DevRecvBuf[(m_DevRecvDealIndex + 2) % DevRecvBuf.Length]));
                    dataLen2 = ((DevRecvBuf[(m_DevRecvDealIndex + 3) % DevRecvBuf.Length] & 0xFC) / 4
                              + 0x40 * (DevRecvBuf[(m_DevRecvDealIndex + 4) % DevRecvBuf.Length]));

                    //两个长度L不一致
                    if (dataLen1 != dataLen2)
                    {
                        m_DevRecvDealIndex = (m_DevRecvDealIndex + 1) % DevRecvBuf.Length;
                        continue;
                    }

                    //数据区长度错误
                    int dataAreaLen = dataLen1;
                    if (dataAreaLen >= 1000)
                    {
                        m_DevRecvDealIndex = (m_DevRecvDealIndex + 1) % DevRecvBuf.Length;
                        continue;                                   //继续寻找
                    }

                    //报文未接收完整，，继续接收
                    if (recvDataLen < (dataAreaLen + 8))
                    {
                        return;
                    }

                    //帧尾0x16

                    if (DevRecvBuf[(m_DevRecvDealIndex + 7 + dataAreaLen) %DevRecvBuf.Length] != 0x16)
                    {
                        m_DevRecvDealIndex = (m_DevRecvDealIndex + 1) % DevRecvBuf.Length; //无帧尾，非正常数据，丢弃第一个字节
                        continue;                                   //继续寻找
                    }

                    //累加和校验
                    byte checksum;
                    byte[] data1frame = new byte[dataAreaLen];
                    for (int i = 0; i < data1frame.Length; i++)
                    {
                        data1frame[i] = DevRecvBuf[(m_DevRecvDealIndex + 6 + i) % DefaultDevCapacity];
                    }

                    //检查校验码 从第一个0X68到CS前所有字节的和模256校验
                    checksum = CheckSum(data1frame, 0, dataAreaLen);
                    if (checksum != DevRecvBuf[(m_DevRecvDealIndex + 6 + dataAreaLen) % DefaultDevCapacity])
                    {
                        m_DevRecvDealIndex = (m_DevRecvDealIndex + 1) % DefaultDevCapacity; //校验和不正确，非正常数据，丢弃第一个字节
                        continue;                                   //继续寻找帧头
                    }

                    //数据解释
                    byte[] data2frame = new byte[dataAreaLen + 8];

                    for (int i = 0; i < data2frame.Length; i++)
                    {
                        data2frame[i] = DevRecvBuf[(m_DevRecvDealIndex + i) % DefaultDevCapacity];
                    }

                    //链路检测帧,报文日志的记录已在DevTcpClient链路帧回应时处理，此处跳过

                    ExplainData(data2frame);

                    m_DevRecvDealIndex = (m_DevRecvDealIndex + 8 + dataAreaLen) % DefaultDevCapacity;
                    return;
                }
                return;
            }
        }

        public void ExplainData(byte[] bFrame)
        {
            if (bFrame.Length >= 20)
            {

                if ( (bFrame[1] & 0x01) == 0x01)
                {
                    string strAddr = (bFrame[9] + bFrame[10] * 256).ToString();
                    string strTermAddr = GetStrFromHex(bFrame[8]) + GetStrFromHex(bFrame[7]) + strAddr;

                    if (GlbData.GetProtocolID(strTermAddr) == "93")
                    {
                        ExplainData130(bFrame);
                        return;
                    }
                }

                byte bCtrlWord = bFrame[6];

                if ((bCtrlWord & 0x40) > 0)
                {
                    if (bFrame[12] == 0x02)
                    {
                        //链接帧数据，由IOT平台代发ACK，这里不再下发
                        return;
                    }

                    //主站主动下发
                    List<CommandPara> lsCmdPars = new List<CommandPara>();

                    string strCmdFrame = "";

                    for (int i = 0; i < bFrame.Length; i++)
                    {
                        strCmdFrame += GetStrFromHex(bFrame[i]);
                    }

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

                    string strTermAddr = GetStrFromHex(bFrame[8]) + GetStrFromHex(bFrame[7]) + GetStrFromHex(bFrame[10]) + GetStrFromHex(bFrame[9]);
                    string strCTIOTID = "";

                    string strDeviceID = GlbData.GetDeviceID(strTermAddr, ref strCTIOTID);

                    if (strDeviceID == "") return;

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

                                if ( GlbData.DBConn.ExeSQL(strSQL) )
                                {
                                    ctiot_app_para.TOKEN = strToken;
                                    ctiot_app_para.LAST_GETTOKEN_TIME = strLastgetTokenTime;

                                    GlbData.CTIOTAppParaList[strCTIOTID] = ctiot_app_para;
                                }
                            }

                            string result = currsdk.sendCommand(strToken, strDeviceID, ctiot_app_para.CALLBACKURL, "CmdService", "Cmd_Down", lsCmdPars);
                            if (result == null)
                            {
                                return;
                            }
                        }
                    }
                    else
                        return;
                }
                else
                {
                    //主站对设备请求的响应
                }
            }
            return;
        }

        public void ExplainData130(byte[] bFrame)
        {
            //主站主动下发
            List<CommandPara> lsCmdPars = new List<CommandPara>();

            string strCmdFrame = "";

            for (int i = 0; i < bFrame.Length; i++)
            {
                strCmdFrame += GetStrFromHex(bFrame[i]);
            }

            CommandPara currCmdPara = new CommandPara();
            currCmdPara.isNum = false;
            currCmdPara.paraName = "array";
            currCmdPara.paraValue = strCmdFrame;
            lsCmdPars.Add(currCmdPara);

            string strAddr = (bFrame[9] +bFrame[10] * 256).ToString();
            string strTermAddr = GetStrFromHex(bFrame[8]) + GetStrFromHex(bFrame[7]) + strAddr;
            string strCTIOTID = "";

            string strDeviceID = GlbData.GetDeviceID(strTermAddr, ref strCTIOTID);

            if (strDeviceID == "") return;

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

                    string result = currsdk.sendCommand(strToken, strDeviceID, ctiot_app_para.CALLBACKURL, "DeliverySchedule", "BATTERY_WARNING", lsCmdPars);
                    if (result == null)
                    {
                        return;
                    }
                }
            }
            else
                return;
        }

        public int PickOut188Frame()
        {
            if (m_DevRecvIndex > m_DevRecvDealIndex) //表示有接收到数据
            {
                //检出有效帧
                while (((m_DevRecvIndex + DefaultDevCapacity - m_DevRecvDealIndex) % DefaultDevCapacity) > 0)
                {
                    //帧头判断
                    if (DevRecvBuf[(m_DevRecvDealIndex + 0) % DefaultDevCapacity] != 0x68)
                    {
                        m_DevRecvDealIndex = (m_DevRecvDealIndex + 1) % DefaultDevCapacity; //检测不到帧头，非正常数据，丢弃本字节
                        continue;                                   //继续寻找帧头
                    }

                    int recvDataLen = (m_DevRecvIndex + DevRecvBuf.Length - m_DevRecvDealIndex) % DevRecvBuf.Length;

                    //小于帧基本长度，继续接收
                    if (recvDataLen < 13)
                    {
                        return -1;
                    }

                    //判断第二个字节0x10
                    if (DevRecvBuf[(m_DevRecvDealIndex + 1) % DefaultDevCapacity] != 0x10)
                    {
                       return -1;                                   //继续寻找
                    }

                    //数据区长度
                    int dataLen = 0;

                    dataLen = DevRecvBuf[(m_DevRecvDealIndex + 10) % DefaultDevCapacity];

                    //数据区长度错误
                    if (dataLen >= 200)
                    {
                        return -1;
                    }

                    //报文未接收完整，，继续接收
                    if (recvDataLen < (dataLen + 13))
                    {
                        return -1;
                    }

                    //帧尾0x16
                    if (DevRecvBuf[(m_DevRecvDealIndex + 12 + dataLen) % DefaultDevCapacity] != 0x16)
                    {
                        return -1; 
                    }

                    //累加和校验
                    byte checksum;
                    byte[] dataframe = new byte[dataLen + 13];
                    for (int i = 0; i < dataframe.Length; i++)
                    {
                        dataframe[i] = DevRecvBuf[(m_DevRecvDealIndex + i) % DefaultDevCapacity];
                    }

                    //检查校验码
                    checksum = CheckSum(dataframe, 0, dataLen + 11);

                    if (checksum != DevRecvBuf[(m_DevRecvDealIndex + 11 + dataLen) % DefaultDevCapacity])
                    {
                        return -1;
                    }

                    //链路检测帧,报文日志的记录已在DevTcpClient链路帧回应时处理，此处跳过

                    ExplainData188(dataframe);

                    m_DevRecvDealIndex = (m_DevRecvDealIndex + 13 + dataLen) % DefaultDevCapacity;
                    return 0;
                }
                return 0;
            }
            return 0;
        }

        public void ExplainData188(byte[] bFrame)
        {
            if (bFrame.Length < 13) return;

            if (!(bFrame[11] == 0x30 && bFrame[12] == 0x11)) return;
            //主站主动下发
            List<CommandPara> lsCmdPars = new List<CommandPara>();

            string strCmdFrame = "";

            for (int i = 0; i < bFrame.Length; i++)
            {
                strCmdFrame += GetStrFromHex(bFrame[i]);
            }

            CommandPara currCmdPara = new CommandPara();
            currCmdPara.isNum = true;
            currCmdPara.paraName = "L";
            currCmdPara.paraValue =( strCmdFrame.Length/2 ).ToString();
            lsCmdPars.Add(currCmdPara);

            currCmdPara = new CommandPara();
            currCmdPara.isNum = false;
            currCmdPara.paraName = "MOT";
            currCmdPara.paraValue = strCmdFrame;
            lsCmdPars.Add(currCmdPara);

            string strTermAddr = GetStrFromHex(bFrame[8]) + GetStrFromHex(bFrame[7]) + GetStrFromHex(bFrame[6]) + GetStrFromHex(bFrame[5]) +
                GetStrFromHex(bFrame[4]) + GetStrFromHex(bFrame[3]) + GetStrFromHex(bFrame[2]);
            string strCTIOTID = "";

            string strDeviceID = GlbData.GetDeviceID(strTermAddr, ref strCTIOTID);

            if (strDeviceID == "") return;

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

                    string result = currsdk.sendCommand(strToken, strDeviceID, ctiot_app_para.CALLBACKURL, "DATA_UP", "MOT_DOWN", lsCmdPars);
                    if (result == null)
                    {
                        return;
                    }
                }
            }
        }

        protected void SendDataEnd(IAsyncResult iar)
        {
            Socket client;
            try
            {
                lock (ModifyLock)
                {
                    if (DevStatus == DeviceStatus.Close || DevStatus == DeviceStatus.Malfunction)
                        return;

                    client = (Socket)iar.AsyncState;

                    if (client == null)
                    {
                        Opened = false;
                        DevStatus = DeviceStatus.Close;

                        return;
                    }

                    client.EndSend(iar);

                    iLastSendState = 1;

                    LastSendTime = DateTime.Now;
                }
            }
            catch (Exception e)
            {
                //GlbData.CommErrLogToFile(e.TargetSite + " : " + e.Message);

                Opened = false;
                DevStatus = DeviceStatus.Close;

            }
        }

        /// <summary>
        /// 发送数据
        /// </summary>
        /// <param name="buffer"></param>
        /// <param name="offset"></param>
        /// <param name="count"></param>
        /// <returns></returns>
        public int WriteDevice(byte[] buffer, int offset, int count)
        {
            iLastSendState = 0;
            lock (mSendLock)
            {
                try
                {
                    if (ClientSock != null && (DevStatus == DeviceStatus.Ready || DevStatus == DeviceStatus.Opened))
                    {
                        ClientSock.BeginSend(buffer, offset, count, SocketFlags.None, new AsyncCallback(SendDataEnd), ClientSock);
                        return count;
                    }
                    else
                        return 0;
                }
                catch (Exception e)
                {
                    return 0;
                }
            }
        }


        /// <summary>
        /// 
        /// <summary>
        /// 16进制字符串转换成16进制Byte："D6"-->0xD6
        /// </summary>
        /// <param name="pChar"></param>
        /// <returns></returns>
        public static byte GetByteFromstrHex(string pChar)
        {
            byte byteBuf = 0;
            if (pChar.Length > 0)
            {
                if (pChar[0] >= '0' && pChar[0] <= '9') byteBuf = (byte)((int)pChar[0] - '0');
                else if (pChar[0] >= 'A' && pChar[0] <= 'F') byteBuf = (byte)(((int)pChar[0] - 'A') + 10);
                else if (pChar[0] >= 'a' && pChar[0] <= 'f') byteBuf = (byte)(((int)pChar[0] - 'a') + 10);
                else byteBuf = 0;
            }
            if (pChar.Length > 1)
            {
                if (pChar[1] >= '0' && pChar[1] <= '9') byteBuf = (byte)((int)byteBuf * 0X10 + ((int)pChar[1] - '0'));
                else if (pChar[1] >= 'A' && pChar[1] <= 'F') byteBuf = (byte)((int)byteBuf * 0X10 + ((int)pChar[1] - 'A') + 10);
                else if (pChar[1] >= 'a' && pChar[1] <= 'f') byteBuf = (byte)((int)byteBuf * 0X10 + ((int)pChar[1] - 'a') + 10);
                else byteBuf = (byte)((int)byteBuf * 0X10);
            }
            return byteBuf;
        }

        /// <summary>
        /// 求和校验 
        /// </summary>
        /// <param name="Buf"></param>
        /// <param name="begin"></param>
        /// <param name="Len"></param>
        /// <returns></returns>
        public static byte CheckSum(byte[] Buf, int begin, int Len)
        {
            byte Sum = 0;
            for (int i = begin; i < begin + Len; i++) Sum += Buf[i];
            return Sum;
        }

        /// <summary>
        /// 16进制Byte转换成16进制字符串：,0xD6-->'D','6'  0x01-->'0','1'
        /// </summary>
        /// <param name="bHex"></param>
        /// <returns></returns>
        public static string GetStrFromHex(byte bHex)
        {
            string strTemp = "";
            strTemp = ((bHex >> 4)).ToString("X");
            strTemp += (bHex & 0x0F).ToString("X");
            return strTemp;

        }
    }

    public class ClientId
    {
        private int _id;            // 与DevTcpClient对象的Socket对象的Handle值相同,必须用这个值来初始化它 


        #region construtor
        public ClientId(int socketHandle)           //Socket的Handle值
        {
            _id = socketHandle;
        }
        #endregion construtor

        #region override
        public override string ToString()           // 重载,为了方便显示输出 
        {
            return _id.ToString();
        }
        #endregion override

        #region property
        public int ID
        {
            get
            {
                return _id;
            }
        }
        #endregion property
    }
}
