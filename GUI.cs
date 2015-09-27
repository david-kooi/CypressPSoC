using System;
//using System.IO;
using System.Collections.Generic;
using System.Collections;
using System.ComponentModel;
using System.Data;
using System.Drawing;
//using System.Linq;
using System.Text;
//using System.Threading.Tasks;
using System.Windows.Forms;
using System.Diagnostics;
using System.Threading;
using CyUSB;


namespace scalemonitor
{


    public partial class Form1 : Form
    {
        // the constants below are also defined on the psoc and are passed to configure and control it.
        // Scale constants
        //mode byte mask bits
        
        const byte INIT_SCALE_VARS = (0x01);
        const byte GET_SCALE_LOW_LIMIT = (0x02);
        const byte GET_SCALE_HIGH_LIMIT = (0x04);
        const byte SEND_SCALE_WEIGHT = (0x08);
        const byte GET_CUSTOM_DATA = (0x10);

        //var byte mask bits
        const byte MAX_WEIGHT_VAR_MASK_BIT =  (0x01);
        const byte PRECISION_VAR_MASK_BIT = (0x02);
        const byte AUTO_MODE_MASK_BIT = (0x04);  //low is fixed , high is auto
        const byte CONTINOUS_MODE_MASK_BIT = (0x08);

        //  in buffer array byte values
        const byte MODE_BYTE = (0x00);
        const byte SET_VARS_MASK_BYTE = (0x01);
        const byte MAX_WEIGHT_VAR_BYTE = (0x02);
        const byte PRECISION_VAR_BYTE = (0x03);
        const byte CUSTOM_DATA_START = (0x08);
        const byte CUSTOM_DATA_MAX_LENGTH = (0x20);

        //constants used on host only
        const byte FIXED_MODE_DIGITS_LENGTH = 4;

        //GLOBAL VARIABLES and CyUSB.dll Class Instances
        CyUSBDevice loopDevice = null;
        USBDeviceList usbDevices = null;
        CyBulkEndPoint inEndpoint = null;
        CyBulkEndPoint outEndpoint = null;


        bool bRunning = false;
        long  inCount;
        const int XFERSIZE = 64;
        byte[] outData = new byte[XFERSIZE];
        byte[] inData = new byte[XFERSIZE];

  
   /*the first part of the code configures the USB connection to the Psoc5lp
   * based on the driver cyusb.sys and inf configuration file cyusb.inf
   * cyusb.inf sets a VID of 0x04b4 and PID of 0x0010 which are configured
   * in the psoc USB device.  these drivers files are in the psoc project folder.
    * 
    * All the USB code uses the CyUSB.DLL file which this program includes
   */

       public Form1()
        {
            InitializeComponent();

            // Create a list of CYUSB devices
            usbDevices  = new USBDeviceList(CyConst.DEVICES_CYUSB);

            //Adding event handlers for device attachment and device removal
            usbDevices.DeviceAttached += new EventHandler(usbDevices_DeviceAttached);
            usbDevices.DeviceRemoved += new EventHandler(usbDevices_DeviceRemoved);

            //The below function sets the device with particular VID and PId and searches for the device with the same VID and PID.
            setDevice();
        }

       private void Form1_Load(object sender, EventArgs e)
       {
           //nothing yet
       }

       /* Summary
   closing the open form
*/
       private void Form1_FormClosing(object sender, FormClosingEventArgs e)
       {
           // If close was selected while running the loopback, shut it down.
           if (bRunning)
               //btnStop_Click(this, null);

           if (usbDevices != null) usbDevices.Dispose();
       }

        /* Summary
            This is the event handler for Device removal event.
        */
        void usbDevices_DeviceRemoved(object sender, EventArgs e)
        {
            setDevice();
        }


        /* Summary
            This is the event handler for Device Attachment event.
        */
        void usbDevices_DeviceAttached(object sender, EventArgs e)
        {
            setDevice();
        }


        /* Summary
            The function sets the device, as the one having VID=04b4 and PID=1004
            This will detect only the devices with the above VID,PID combinations
        */
        public void setDevice()
        {

            loopDevice = usbDevices[0x04b4, 0x0010] as CyUSBDevice;

            btnSendConfiguration.Enabled = (loopDevice != null);

            sendDeviceStateToFromHeader();

            // Set the IN and OUT endpoints per the selected radio buttons.
            if (loopDevice != null)
            {

                outEndpoint = loopDevice.EndPointOf(0x02) as CyBulkEndPoint;//0x00 is out + EP2 = 0x02
                inEndpoint = loopDevice.EndPointOf(0x81) as CyBulkEndPoint; //0x80 is in + EP1 = 0x81
 
                outEndpoint.TimeOut = 1000;
                inEndpoint.TimeOut = 1000;
            }
        }

        public void sendDeviceStateToFromHeader()
        {
            if (loopDevice != null)
                Text = "Scale Monitor using " + loopDevice.FriendlyName;
            else
                Text = "Scale Monitor - the Cypress USB device not found";
        }
//The Code below communicates to the Cypress Psoc5LP Kit for control of the scale firmware
//The Psoc5lp is connected to the scale loadcell via the onboard analog ADC and passes data via usb
//The Psoc5lp waits for commands from this host program to configure and initiate measurements
//The Psoc5lp performs the ADC convertion to weight and sends an ASCII buffer back
// the constants below are also defined on the psoc and are passed to configure and control it.

        // Scale constants defined at top
        //mode byte mask bits
        /*
        const byte INIT_SCALE_VARS = (0x01);
        const byte GET_SCALE_LOW_LIMIT = (0x02);
        const byte GET_SCALE_HIGH_LIMIT = (0x04);
        const byte SEND_SCALE_WEIGHT = (0x08);
        const byte GET_CUSTOM_DATA = (0x10);

        //var byte mask bits
        const byte MAX_WEIGHT_VAR_MASK_BIT = (0x01);
        const byte PRECISION_VAR_MASK_BIT = (0x02);
        const byte AUTO_MODE_MASK_BIT = (0x04);  //low is fixed , high is auto
        const byte CONTINOUS_MODE_MASK_BIT = (0x08);
         * 
        //  in buffer array byte values
        const byte MODE_BYTE = (0x00);
        const byte SET_VARS_MASK_BYTE = (0x01);
        const byte MAX_WEIGHT_VAR_BYTE = (0x02);
        const byte PRECISION_VAR_BYTE = (0x03);
        const byte CUSTOM_DATA_START = (0x08);
        const byte CUSTOM_DATA_MAX_LENGTH = (0x20);
        */
        private void btnSendConfiguration_Click(object sender, EventArgs e)
        {
            byte scale_state_mode, var_mask_bits;
            bool result=false;
            var_mask_bits = 0;

            if (radioBtnGetWeight.Checked)
            {
                scale_state_mode = SEND_SCALE_WEIGHT;
                outData[MODE_BYTE] = scale_state_mode;

                if (chkContinousMode.Checked)
                    var_mask_bits = CONTINOUS_MODE_MASK_BIT;

                outData[SET_VARS_MASK_BYTE] = var_mask_bits;
                result=TransferData();
                if (result == true)
                {
                    result = ReceiveData();
                    DisplayReceivedData(result);
                }
            }
            else if (radioBtnInitAutoVars.Checked)
            {
                if (chkAutoMode.Checked)
                {
                    scale_state_mode = INIT_SCALE_VARS;
                    outData[MODE_BYTE] = scale_state_mode;
                    var_mask_bits = AUTO_MODE_MASK_BIT;
                    if (txtMaxWeight.Text != "")
                        var_mask_bits |= MAX_WEIGHT_VAR_MASK_BIT;
                    if (txtPrecision.Text != "")
                        var_mask_bits |= PRECISION_VAR_MASK_BIT;

                    outData[SET_VARS_MASK_BYTE] = var_mask_bits;
                    outData[MAX_WEIGHT_VAR_BYTE] = Convert.ToByte(txtMaxWeight.Text);
                    outData[PRECISION_VAR_BYTE] = Convert.ToByte(txtPrecision.Text);
                    result = TransferData();
                }
              
            }
            else if (radioBtnMinWeight.Checked)
            {
                scale_state_mode = GET_SCALE_LOW_LIMIT;
                outData[MODE_BYTE] = scale_state_mode;

                result = TransferData();
                if (result == true)
                {
                    result = ReceiveData();
                    if (result == true)
                        lblWeight.Text = "ADC=" + inData[0].ToString();

                }

            }
            else if (radioBtnMaxWeight.Checked)
            {
                scale_state_mode = GET_SCALE_HIGH_LIMIT;
                outData[MODE_BYTE] = scale_state_mode;
                result = TransferData();
                if (result == true)
                {
                    result = ReceiveData();
                    if (result == true)
                        lblWeight.Text = "ADC=" + inData[0].ToString();
                }

            }
            else if (radioBtnCustomSendData.Checked)
            {
                clear_outdata();
                scale_state_mode = GET_CUSTOM_DATA;
                outData[MODE_BYTE] = scale_state_mode;
                GetCustomOutputData();
                result = TransferData();
 
            }

            if (chkContinousMode.Checked == true && (result == true))
                timer1.Start();
            else
                timer1.Stop();
        }

/*Custom data is used to allow more configurations of the psoc that are not
 * currently defined on the user interface
The data comes from the bottom textbox on the main form*/
        private void GetCustomOutputData()
        {

            //string[] m_textbox;
            //string[] stringSeparators = new string[] { " " };
            //m_textbox = gettext.Split(stringSeparators, StringSplitOptions.RemoveEmptyEntries);

            char value;
            string gettext;

            gettext = txtLBDataSend.Text;

            int x, i;
            x = gettext.Length;

            for (i = CUSTOM_DATA_START; i < x && i < CUSTOM_DATA_MAX_LENGTH; i++)
            {
                value = gettext[i];
                outData[i] = (byte)value;
            }

        }


//outdata is a global array and is used to send data to the psoc
        public void clear_outdata()
        {
            for (int i = 0; i < XFERSIZE; i++)
                outData[i] = 0;
        }

//the psoc usb expects fixed length transfers so fill with ascii blanks 32 as needed
        public int outdata_non_zero_elements()
        {
            int cnt = 0;
            for (int i = 0; i < XFERSIZE; i++)
            {
                if ((int)outData[i] > 0)
                    cnt = cnt + 1;
                else
                    outData[i] = 32;
            }
            return cnt;
        }


        /* Summary
            transmit data using the CyUSB.dll, the psoc waits for this to do anything 
        */
        public bool TransferData()
        {
            int xferLen = XFERSIZE;
            bool bResult = true;
            sendDeviceStateToFromHeader();

            if (outdata_non_zero_elements() > 0)
            {
                xferLen = XFERSIZE;
                //calls the XferData function for bulk transfer(OUT) in the cyusb.dll
                bResult = outEndpoint.XferData(ref outData, ref xferLen);
            }
            if (bResult == false)
                Text = "Transmit Failure";

            return bResult;
        }

        /* Summary
            after a transmit data using the CyUSB.dll, a receive is sent to get response data 
        */
        public bool ReceiveData()
        {
            int xferLen = XFERSIZE;
            bool bResult;
            sendDeviceStateToFromHeader();

            //calls the XferData function for bulk transfer(OUT/IN) in the cyusb.dll
            bResult = inEndpoint.XferData(ref inData, ref xferLen);
            inCount += xferLen;

            if (bResult == false)
                Text = "Receive Failure";

            return bResult;

        }

        /*Summary
         * Display received data
         */
        void DisplayReceivedData(bool result)
        {
            int xferLen = 0, i;

            if (result == true)
            {

                if (chkAutoMode.Checked && xferLen > 0)
                    xferLen = Convert.ToByte(txtPrecision.Text); //actual data length
                else
                    xferLen = FIXED_MODE_DIGITS_LENGTH;

                lblWeight.Text = "";
                if (result == true)
                {
                    for (i = 0; i < xferLen; i++)
                        lblWeight.Text = lblWeight.Text + (char)inData[i];
                }                  
            }
        }

        /* Summary
          the timer is used for continous receives from the psoc
          when the psoc is in continous mode
        */
        private void timer1_Tick(object sender, EventArgs e)
        {

            bool result = false;

            if (chkContinousMode.Checked)
            {
                result = ReceiveData();
                DisplayReceivedData(result);
            }
            else
                timer1.Stop();

        }

        /* Summary
          auto mode is set in the psoc to calculate auto range scale
          this allows for variable max weight, precision and compensation for ADC offsets
          if not in auto then the scale just calculates two digits upto 1.99lb
          and doesn't attempt to compensate for ADC offsets
        */
        private void chkAutoMode_CheckedChanged(object sender, EventArgs e)
        {
            if (chkAutoMode.Checked)
            {
                txtMaxWeight.Enabled = true;
                txtPrecision.Enabled = true;
                radioBtnInitAutoVars.Enabled = true;
                radioBtnMaxWeight.Enabled = true;
                radioBtnInitAutoVars.Enabled = true;
                radioBtnMinWeight.Enabled = true;
            }
                else
            {
                txtMaxWeight.Enabled = false;
                txtPrecision.Enabled = false;
                radioBtnInitAutoVars.Enabled = false;
                radioBtnMaxWeight.Enabled = false;
                radioBtnInitAutoVars.Enabled = false;
                radioBtnMinWeight.Enabled = false;

            }
        }

        private void chkContinousMode_CheckedChanged(object sender, EventArgs e)
        {
            if (chkContinousMode.Checked != true)
                timer1.Stop();
        }

//*************************Spare Code****************************
        /*  outData needs to be 64 bytes this cuts it short*/
        /*
        public int non_zero_elements(ref byte[] m_array)
        { int cnt=0;
        for (int i = 0; i < XFERSIZE; i++)
            {
                if ((int)m_array[i] > 0)
                    cnt = cnt + 1;
                else
                    m_array[i] = 32;
            }
            return cnt;//>0? true:false;
        }*/


    }
}
