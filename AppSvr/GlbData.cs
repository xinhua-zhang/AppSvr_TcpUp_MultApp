using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Data;
using System.Collections;

namespace AppSvr
{
    public static class GlbData
    {
        private static int DBType = 1;//数据库连接方式 1 -- sqlserver
        private static string connStr = "";
        private static DBLogic.CommDataAccess _DBConn = null;

        private static string fileName = Application.StartupPath + "\\HttpSvrCfg.xml";

        private static Config.HlLoRaCfg cfg = new Config.HlLoRaCfg(fileName);

        private static string _ServerIPPort = "127.0.0.1:11118";

        public static Hashtable CTIOTAppParaList = new Hashtable();

        public static string _LocalHttpSvrIPPort = "127.0.0.1:10099";

        public static Hashtable NBCtrlCmdList = new Hashtable();

        public static DBLogic.CommDataAccess DBConn
        {
            get
            {
                if (_DBConn == null || connStr == "")
                {
                    try
                    {
                        connStr = cfg.ReadDBPara("sqlserver", "Data Source =.; UID = sa; PWD = holley; initial catalog = PUAMRDB_Empty");
                        _DBConn = new DBLogic.CommDataAccess(DBType, connStr, "");

                    }
                    catch
                    {
                        _DBConn = null;
                    }
                }

                return _DBConn;
            }
        }

        public static string ServerIPPort
        {
            get
            {
                try
                {
                    _ServerIPPort = cfg.ReadDBPara("ServerIPPort", "127.0.0.1:11118");
                }
                catch
                {
                }

                return _ServerIPPort;
            }
        }

        public static string LocalHttpSvrIPPort
        {
            get
            {
                try
                {
                    _LocalHttpSvrIPPort = cfg.ReadDBPara("LocalHttpSvrIPPort", "127.0.0.1:10099");
                }
                catch
                {
                }

                return _LocalHttpSvrIPPort;
            }
        }

        public static void GetNAInfo()
        {
            string strSQL = "select * from CT_IOT_APP_PARA";
            DataTable dtInfo = GlbData.DBConn.GetTable(strSQL);

            if (dtInfo != null && dtInfo.Rows.Count > 0)
            {
                for (int i = 0; i < dtInfo.Rows.Count; i++)
                {
                    CTIOT_APP_PARA ctiot_app_para = new CTIOT_APP_PARA();
                    string strID = dtInfo.Rows[i]["ID"].ToString().Trim();
                    ctiot_app_para.SVR_IP = dtInfo.Rows[i]["SVR_IP"].ToString().Trim();
                    ctiot_app_para.SVR_PORT = int.Parse(dtInfo.Rows[i]["SVR_PORT"].ToString().Trim());
                    ctiot_app_para.APP_ID = dtInfo.Rows[i]["APP_ID"].ToString().Trim();
                    ctiot_app_para.APP_PWD = dtInfo.Rows[i]["APP_PWD"].ToString().Trim();
                    ctiot_app_para.CERT_PWD = dtInfo.Rows[i]["CERT_PWD"].ToString().Trim();
                    ctiot_app_para.CERT_FILE = dtInfo.Rows[i]["CERT_FILE"].ToString().Trim();
                    ctiot_app_para.TOKEN = dtInfo.Rows[i]["TOKEN"].ToString().Trim();
                    ctiot_app_para.LAST_GETTOKEN_TIME = dtInfo.Rows[i]["LAST_GETTOKEN_TIME"].ToString().Trim();
                    ctiot_app_para.CALLBACKURL = dtInfo.Rows[i]["CALLBACKURL"].ToString().Trim();

                    if (!CTIOTAppParaList.Contains(strID))
                        CTIOTAppParaList.Add(strID, ctiot_app_para);
                }
            }
            else
            {
            }
        }

        private static string GetParaValue(DataTable dtInfo, string strParaName, string strDefaultValue)
        {
            for (int i = 0; i < dtInfo.Rows.Count; i++)
            {
                string _paraName = dtInfo.Rows[i]["PARA_NAME"].ToString().Trim();
                if (_paraName == strParaName.Trim())
                {
                    string strParaValue = dtInfo.Rows[i]["PARA_VALUE"].ToString().Trim();
                    return strParaValue;
                }
            }

            return strDefaultValue;
        }

        public static string GetDeviceID(string strTermAddr, ref string strCTIOTID)
        {
            string strSQL = "select pmp.deviceId,pcp.CT_IOT_ID from para_mtr_point_pu pmp,para_concen_pu pcp  where pmp.concen_id = pcp.concen_id and  pcp.DEVICE_ADDR='" + strTermAddr + "' ";
            DataTable dtInfo = GlbData.DBConn.GetTable(strSQL);
            string strDeviceID = "";

            if (dtInfo != null && dtInfo.Rows.Count > 0)
            {
                strDeviceID = dtInfo.Rows[0]["deviceId"].ToString().Trim();

                strCTIOTID = dtInfo.Rows[0]["CT_IOT_ID"].ToString().Trim();
            }
            else
            {
                strDeviceID = "";
                strCTIOTID = "";
            }

            return strDeviceID;
        }

        public static string GetProtocolID(string strTermAddr)
        {
            string strSQL = "select pcp.PROTOCOL_TYPE from para_concen_pu pcp  where  pcp.DEVICE_ADDR='" + strTermAddr + "' ";
            DataTable dtInfo = GlbData.DBConn.GetTable(strSQL);
            string strPROTOCOL_TYPE = "";

            if (dtInfo != null && dtInfo.Rows.Count > 0)
            {
                strPROTOCOL_TYPE = dtInfo.Rows[0]["PROTOCOL_TYPE"].ToString().Trim();
            }
            else
            {
                strPROTOCOL_TYPE = "";
            }

            return strPROTOCOL_TYPE;
        }
    }

    public class CTIOT_APP_PARA
    {
        /// <summary>
        /// 
        /// </summary>
        public string APP_ID { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public string APP_PWD { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public string SVR_IP { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public int SVR_PORT { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public string CERT_FILE { get; set; }

        public string CERT_PWD { get; set; }

        public string TOKEN { get; set; }

        public string LAST_GETTOKEN_TIME { get; set; }

        public string CALLBACKURL { get; set; }
    }
}
