using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net;
using System.Net.Sockets;

namespace AppSvr
{
    public class DevUdpClient
    {
        #region 字段
        /// <summary>
        /// 接收缓冲长度   10K
        /// </summary>
        private const int DefaultCapacity = 10240;

        /// <summary>
        /// 裁定为无法绑定通道的无效Socket的时间
        /// </summary>
        public DateTime CheckNotValidTime = DateTime.Now;

        //
        public DateTime SocketFirstBindChannelTime = DateTime.Now;


        /// <summary> 
        /// 服务器发送到客户端的数据
        /// </summary> 
        private byte[] RecvBuffer;						//接收缓冲区
        private int m_RecvIndex;						//接收索引
        private int m_RecvDealIndex;					//接收处理索引

        /// <summary> 
        /// 每次接受报文的最大长度1024 
        /// </summary> 
        private const int DefaultBufferSize = 1024;
        /// <summary> 
        /// 每次接受报文数组
        /// </summary> 
        private byte[] RecvDataBuffer;

        /// <summary> 
        /// 客户端的Socket  客户端连接服务器端时赋值
        /// </summary> 
        private Socket ClientSock;

        /// <summary> 
        /// IP+Port 
        /// </summary> 
        private IPEndPoint iep;

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

        ///////////////////////////////////////// GPRS/////////////// 
        /// <summary>
        /// 心跳次数
        /// </summary>
        private int m_nRecvHeartTimes;
        /// <summary>
        /// 是否需要删除
        /// </summary>
        public bool m_bNeedDelete;
        /// <summary>
        /// 最近接受信息时间 用来判断超时 6min
        /// </summary>
        public DateTime LastRcvTime;
        /// <summary>
        /// 延迟次数
        /// </summary>
        public uint m_nDelayToDel;
        /// <summary>
        /// 远程终端手机号码/地址
        /// </summary>
        public string m_sRemoteTerminalAddr;

        public int iLastSendState;

        //////////////////////////////////////// GPRS //////////////
        #endregion

        #region 事件定义

        /// <summary> 
        /// 接收到数据报文事件 
        /// </summary> 
        //public event NetUdpEvent ReceivedDatagram;

        #endregion

        #region 方法

        /// <summary> 
        /// 构造函数 
        /// </summary> 
        /// <param name="cliSock">现有的Socket连接,服务器端Socket</param> 
        public DevUdpClient(string strIPPort)
        {
            string[] commInfo = strIPPort.Split(':');
            string hostname = commInfo[0];

            IPAddress[] hostEntry = Dns.GetHostEntry(hostname).AddressList;
            IPAddress ip;
            for (int i = 0; i < hostEntry.Length; ++i)
            {
                if (hostEntry[i].AddressFamily == AddressFamily.InterNetwork)
                {
                    ip = hostEntry[i];
                    iep = new IPEndPoint(ip, int.Parse(commInfo[1]));
                    break;
                }
            }

            RecvBuffer = new byte[DefaultCapacity];
            m_RecvIndex = 0;
            m_RecvDealIndex = 0;

            RecvDataBuffer = new byte[DefaultBufferSize];

            m_nRecvHeartTimes = 0;

            m_bNeedDelete = false;

            m_nDelayToDel = 0;

            LastRcvTime = DateTime.Now;

            DevStatus = DeviceStatus.Ready;

        }

        public bool OpenDevice()
        {
            try
            {
                ClientSock = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);

                IPEndPoint ep = new IPEndPoint(IPAddress.Any, 0);
                EndPoint tempEP = (EndPoint)ep;

                ClientSock.Bind(tempEP);

                DevStatus = DeviceStatus.Opened;

                ClientSock.BeginReceiveFrom(RecvDataBuffer, 0, DefaultBufferSize, SocketFlags.None, ref tempEP, RecvData, ClientSock);

                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary> 
        /// 数据接收处理函数 
        /// </summary> 
        /// <param name="iar">异步Socket</param> 
        protected void RecvData(IAsyncResult iar)
        {
            Socket remote = (Socket)iar.AsyncState;

            try
            {
                IPEndPoint ep = new IPEndPoint(IPAddress.Any, 0);
                EndPoint tempEP = (EndPoint)ep;

                int recv = remote.EndReceiveFrom(iar, ref tempEP);

                //正常的退出 
                if (recv == 0)
                {
                    DevStatus = DeviceStatus.Close;
                    CloseDevice();
                    return;
                }

                SaveRcv(RecvDataBuffer, 0, recv);

                //继续接收数据 
                ClientSock.BeginReceiveFrom(RecvDataBuffer, 0, DefaultBufferSize, SocketFlags.None, ref tempEP, RecvData, remote);

            }
            catch (SocketException ex)
            {
                //客户端退出 
                if (10054 == ex.ErrorCode)
                {
                    DevStatus = DeviceStatus.Close;
                    CloseDevice();
                }
                else
                {
                    throw (ex);
                }
            }
            catch (ObjectDisposedException ex)
            {
                if (ex != null)
                {
                    ex = null;
                    //DoNothing; 
                }
            }
        }
        /// <summary> 
        /// 关闭会话 
        /// </summary> 
        public void CloseDevice()
        {
            if (ClientSock != null)
            {
                try
                {
                    //关闭数据的接受和发送 
                    ClientSock.Shutdown(SocketShutdown.Both);
                    //清理资源 
                    ClientSock.Close();

                    ClientSock = null;

                    DevStatus = DeviceStatus.Close;

                    Opened = false;
                }
                catch (SocketException)
                {
                }
            }
        }
        /// <summary>
        /// 保存接收来的数据 并判断心跳
        /// </summary>
        /// <param name="data"></param>
        /// <param name="offset"></param>
        /// <param name="count"></param>
        public void SaveRcv(byte[] data, int offset, int count)
        {
            lock (this)
            {
                for (int i = 0; i < count; i++)
                {
                    RecvBuffer[m_RecvIndex] = data[offset + i];
                    m_RecvIndex++;
                    m_RecvIndex = m_RecvIndex % DefaultCapacity;
                }
            }
            LastRcvTime = DateTime.Now;        //标志接受到数据
        }

        /// <summary>
        /// 读缓冲区中的数据
        /// </summary>
        /// <param name="data"></param>
        /// <param name="offset"></param>
        /// <param name="count"></param>
        public int ReadDevice(byte[] buffer, int offset, int count)
        {
            try
            {
                int len = 0;

                if (count == 0)
                    len = (m_RecvIndex + DefaultCapacity - m_RecvDealIndex) % DefaultCapacity;
                else
                    len = Math.Min(((m_RecvIndex - m_RecvDealIndex + DefaultCapacity) % DefaultCapacity), count);

                int index = offset;

                for (int i = 0; i < len; i++)
                {
                    buffer[index] = RecvBuffer[m_RecvDealIndex];
                    m_RecvDealIndex++;
                    m_RecvDealIndex = m_RecvDealIndex % DefaultCapacity;
                    index = (index + 1) % buffer.Length;
                }
                return len;
            }
            catch (Exception)
            {
                return -1;
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
            try
            {
                iLastSendState = 0;
                ClientSock.BeginSendTo(buffer, offset, count, SocketFlags.None, (EndPoint)iep, new AsyncCallback(SendDataEnd), ClientSock);

                return count;
            }
            catch
            {
                return 0;
            }
            //}
            
            //return 0;
        }
        /// <summary> 
        /// 发送数据完成处理函数 
        /// </summary> 
        /// <param name="iar">目标客户端Socket</param> 
        protected void SendDataEnd(IAsyncResult iar)
        {
            Socket svr = (Socket)iar.AsyncState;

            try
            {
                int sent = svr.EndSendTo(iar);
                iLastSendState = 1;
            }
            catch
            {

            }
        }

        private static int GetByteForm16HexChar(string strHexChar)
        {
            if (strHexChar == null) return 0xFF;
            if (strHexChar.Length <= 0) return 0xFF;
            if (strHexChar.Length > 2) strHexChar = strHexChar.Substring(0, 2);
            else if (strHexChar.Length == 1) strHexChar = "0" + strHexChar;

            int iRusult = 0;
            int nLen = strHexChar.Length;
            for (int i = 0; i < nLen; i++)
            {
                string strOneChar = strHexChar.Substring(i, 1).ToUpper();
                int iOneResult = 0;
                switch (strOneChar)
                {
                    case "A":
                        iOneResult = 10;
                        break;
                    case "B":
                        iOneResult = 11;
                        break;
                    case "C":
                        iOneResult = 12;
                        break;
                    case "D":
                        iOneResult = 13;
                        break;
                    case "E":
                        iOneResult = 14;
                        break;
                    case "F":
                        iOneResult = 15;
                        break;
                    default:
                        iOneResult = int.Parse(strOneChar);
                        break;
                }
                for (int k = 0; k < nLen - i - 1; k++)
                    iOneResult *= 16;

                iRusult += iOneResult;
            }

            return iRusult;
        }

        /// <summary>
        /// 设备状态 
        /// </summary>
        /// <returns></returns>
        public DeviceStatus GetDeviceStatus()
        {
            return DevStatus;
        }
        /// <summary>
        /// 维护 客户端与服务器端失去联系 尝试重连
        /// </summary>
        public void MaintainDevice()
        {
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
            switch (RequestStatus)
            {
                case DeviceStatus.Opened:
                case DeviceStatus.Ready:

                    OpenDevice();
                    if (Opened)
                    {
                        count = 0;
                        DevStatus = DeviceStatus.Ready;
                    }
                    else
                    {
                        if (count >= retryCount)
                        {
                            DevStatus = DeviceStatus.Malfunction; //置设备状态为损坏标志
                            count = 0;
                            return;
                        }
                        count++;
                    }

                    return;

                case DeviceStatus.Close:

                    CloseDevice();
                    return;
                default:
                    break;
            }
        }

        /// <summary>
        /// 设置切换状态 仅关闭、打开两个状态
        /// </summary>
        public void SetStatus(DeviceStatus deviceStatus)
        {
            RequestStatus = deviceStatus;
        }

        #endregion

        public void ExplainData()
        {
            if (m_RecvIndex > m_RecvDealIndex) //表示有接收到数据
            {
                //检出有效帧
                while (((m_RecvIndex + DefaultCapacity - m_RecvDealIndex) % DefaultCapacity) > 0)
                {
                    //帧头判断
                    if (RecvBuffer[(m_RecvDealIndex + 0) % DefaultCapacity] != 0x68)
                    {
                        m_RecvDealIndex = (m_RecvDealIndex + 1) % DefaultCapacity; //检测不到帧头，非正常数据，丢弃本字节
                        continue;                                   //继续寻找帧头
                    }

                    int recvDataLen = (m_RecvIndex + RecvBuffer.Length - m_RecvDealIndex) % RecvBuffer.Length;

                    //小于帧基本长度，继续接收
                    if (recvDataLen < 14)
                    {
                        return;
                    }
                    //第2个68判断
                    if (RecvBuffer[(m_RecvDealIndex + 5) % DefaultCapacity] != 0x68)
                    {
                        m_RecvDealIndex = (m_RecvDealIndex + 1) % DefaultCapacity;
                        continue;                                   //继续寻找帧头
                    }

                    //数据区长度
                    int dataLen1 = 0, dataLen2 = 0;

                    dataLen1 = ((RecvBuffer[(m_RecvDealIndex + 1) % RecvBuffer.Length] & 0xFC) / 4
                              + 0x40 * (RecvBuffer[(m_RecvDealIndex + 2) % RecvBuffer.Length]));
                    dataLen2 = ((RecvBuffer[(m_RecvDealIndex + 3) % RecvBuffer.Length] & 0xFC) / 4
                              + 0x40 * (RecvBuffer[(m_RecvDealIndex + 4) % RecvBuffer.Length]));

                    //两个长度L不一致
                    if (dataLen1 != dataLen2)
                    {
                        m_RecvDealIndex = (m_RecvDealIndex + 1) % RecvBuffer.Length;
                        continue;
                    }

                    //数据区长度错误
                    int dataAreaLen = dataLen1;
                    if (dataAreaLen >= 1000)
                    {
                        m_RecvDealIndex = (m_RecvDealIndex + 1) % RecvBuffer.Length;
                        continue;                                   //继续寻找
                    }

                    //报文未接收完整，，继续接收
                    if (recvDataLen < (dataAreaLen + 8))
                    {
                        return;
                    }

                    //帧尾0x16

                    if (RecvBuffer[(m_RecvDealIndex + 7 + dataAreaLen) %RecvBuffer.Length] != 0x16)
                    {
                        m_RecvDealIndex = (m_RecvDealIndex + 1) % RecvBuffer.Length; //无帧尾，非正常数据，丢弃第一个字节
                        continue;                                   //继续寻找
                    }

                    //累加和校验
                    byte checksum;
                    byte[] data1frame = new byte[dataAreaLen];
                    for (int i = 0; i < data1frame.Length; i++)
                    {
                        data1frame[i] = RecvBuffer[(m_RecvDealIndex + 6 + i) % DefaultCapacity];
                    }

                    //检查校验码 从第一个0X68到CS前所有字节的和模256校验
                    checksum = CheckSum(data1frame, 0, dataAreaLen);
                    if (checksum != RecvBuffer[(m_RecvDealIndex + 6 + dataAreaLen) % DefaultCapacity])
                    {
                        m_RecvDealIndex = (m_RecvDealIndex + 1) % DefaultCapacity; //校验和不正确，非正常数据，丢弃第一个字节
                        continue;                                   //继续寻找帧头
                    }

                    //数据解释
                    byte[] data2frame = new byte[dataAreaLen + 8];

                    for (int i = 0; i < data2frame.Length; i++)
                    {
                        data2frame[i] = RecvBuffer[(m_RecvDealIndex + i) % DefaultCapacity];
                    }

                    //链路检测帧,报文日志的记录已在DevTcpClient链路帧回应时处理，此处跳过

                    ExplainData(data2frame);

                    m_RecvDealIndex = (m_RecvDealIndex + 8 + dataAreaLen) % DefaultCapacity;
                    return;
                }
                return;
            }
        }

        public void ExplainData(byte[] bFrame)
        {
            if (bFrame.Length >=20)
            {
                byte bCtrlWord = bFrame[6];

                if ((bCtrlWord & 0x40) > 0 )
                {
                    if (bFrame[12] == 0x02)
                    {
                        //链接帧数据，由IOT平台代发ACK，这里不再下发
                        return;
                    }

                    //主站主动下发
                    List<CommandPara> lsCmdPars = new List<CommandPara>();

                    string strCmdFrame = "";

                    for (int i=0;i<bFrame.Length;i++)
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

                                //存储
                                string strSQL = "update CT_IOT_APP_PARA set TOKEN='{0}',LAST_GETTOKEN_TIME='{1}' where ID=" + strCTIOTID;
                                strSQL = string.Format(strSQL, strToken, dtNow.ToString("yyyy-MM-dd HH:mm:ss"));

                                GlbData.DBConn.ExeSQL(strSQL);
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

        #region IDevice接口中 暂不修改的函数

        /// <summary>
        /// 保存日志
        /// </summary>
        public void DebugOutput()
        {

        }

        #endregion

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
        /// 客户端IP+port
        /// </summary>
        public IPEndPoint IPPort
        {
            get
            {
                return iep;
            }
            set
            {
                iep = value;
            }
        }

        #region override

        /// <summary> 
        /// 使用Socket对象的Handle值作为HashCode,它具有良好的线性特征. 
        /// </summary> 
        /// <returns></returns> 
        public override int GetHashCode()
        {
            return (int)ClientSock.Handle;
        }

        /// <summary> 
        /// 返回两个DevTcpClient是否代表同一个客户端 
        /// </summary> 
        /// <param name="obj"></param> 
        /// <returns></returns> 
        public override bool Equals(object obj)
        {
            DevUdpClient rightObj = (DevUdpClient)obj;

            //return (int)ClientSock.Handle == (int)rightObj.ClientSocket.Handle;

            return (IPPort.Address.Equals(rightObj.IPPort.Address) && IPPort.Port.Equals(rightObj.IPPort.Port));
        }

        /// <summary> 
        /// 重载ToString()方法,返回Session对象的特征 
        /// </summary> 
        /// <returns></returns> 
        public override string ToString()
        {
            string result = string.Format("Client:{0},IP:{1}",
            IPPort.ToString(), ClientSock.RemoteEndPoint.ToString());

            //result.C 
            return result;
        }
        #endregion overide
    }

    /// <summary>设备状态定义：用于通道更改设备状态
    /// </summary>
    public enum DeviceStatus
    {
        Close = 0,          //设备关闭
        Opened = 1,         //设备打开 
        Ready = 2,          //设备就绪 
        Busy = 3,           //设备忙  设备在不同状态之间切换，等待执行命令的返回结果时(如拨号，初始化，挂断过程)
        Malfunction = 4,    //设备故障
        ManualReady = 5,    //设备就绪(拨号通道，手动拨号成功)
        ManualClose = 6     //设备关闭(拨号通道,手动关闭)
    }
}
