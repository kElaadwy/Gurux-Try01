using Gurux.Common;
using Gurux.DLMS;
using Gurux.DLMS.Enums;
using Gurux.DLMS.Objects;
using Gurux.Serial;
using System.IO.Ports;

namespace Gurux_Try;
internal class Program
{
    static void Main(string[] args)
    {
        // Initialize GXDLMSClient with necessary parameters
        // Logical name referencing, client and server addresses, authentication level, and password.
        GXDLMSClient client = new GXDLMSClient(true, 0x1, 0x90, Authentication.Low, "00000000", InterfaceType.HDLC);

        client.Priority = Priority.Normal;
        client.ServiceClass = ServiceClass.Confirmed;
        client.GbtWindowSize = 1;
        client.Standard = Standard.DLMS;
        client.ChallengeSize = 0x10;
        client.MaxReceivePDUSize = 0xFFFF;
        client.Settings.EphemeralBlockCipherKey = [0x7A, 0xDF, 0x63, 0x9C, 0xA7, 0x96, 0x32, 0xFC, 0xA3, 0xD7, 0x81, 0x0B, 0xE6, 0x41, 0x6A, 0xBE];
        client.Settings.EphemeralAuthenticationKey = [0x24, 0x5D, 0x0F, 0x1D, 0xF3, 0x1C, 0x43, 0x80, 0x13, 0x5A, 0xC9, 0x1D, 0x4A, 0x22, 0x02, 0x3D];
        client.NegotiatedConformance = Conformance.GeneralBlockTransfer;

        // Initialize GXSerial for serial communication.
        GXSerial media = new GXSerial();

        try
        {
            // Get the first available COM port and assign it to the media.
            media.PortName = GXSerial.GetPortNames().First();

            // Initialize the serial communication parameters.
            //InitSerial(media);
            media.Open();


            // Object to hold the meter's reply data.
            GXReplyData reply = new GXReplyData();
            byte[] data;

            // Send SNRM request to establish HDLC communication.
            data = client.SNRMRequest();
            if (data != null)
            {
                // Read and parse the SNRM response from the meter.
                ReadDLMSPacket(data, reply, client, media);

                Console.WriteLine("SNRMRequest : ");
                Console.WriteLine(reply.Data.ToString());
                Console.WriteLine(reply.Data.ToString(true));
                Console.WriteLine();

                client.ParseUAResponse(reply.Data); // Parse UA response to confirm connection.

                Console.WriteLine("UAResponse : ");
                Console.WriteLine(reply.Data.ToString());
                Console.WriteLine(reply.Data.ToString(true));
                Console.WriteLine();
            }

            // Send AARQ request to establish application association.
            foreach (byte[] it in client.AARQRequest())
            {
                var xx = "";
                xx += BitConverter.ToString(it).Replace("-", " ");

                reply.Clear(); // Clear previous reply data.
                ReadDLMSPacket(it, reply, client, media); // Read AARQ response from the meter.

                Console.WriteLine("AARQRequest : ");
                Console.WriteLine(reply.Data.ToString());
                Console.WriteLine(reply.Data.ToString(true));
                Console.WriteLine();
            }

            // Parse the AARQ response to verify application association establishment.
            client.ParseAAREResponse(reply.Data);

            Console.WriteLine("ParseAAREResponse : ");
            Console.WriteLine(reply.Data.ToString());
            Console.WriteLine(reply.Data.ToString(true));
            Console.WriteLine();


            // Request and parse objects available in the meter.
            //GXDLMSObjectCollection objects = client.ParseObjects(reply.Data, true);

            GXDLMSData serial = new GXDLMSData("0.0.96.1.0.255"); //Serial number                                        
            var meterSerial = client.Read(serial, 2);


            var zzz = BitConverter.ToString(meterSerial[0]).Replace("-", "");

            reply.Clear();

            ReadDLMSPacket(meterSerial[0], reply, client, media);

            var buff = reply.Data.ToString(true);

            Console.WriteLine("SERIAL : ");
            Console.WriteLine(reply.Data.ToString());
            Console.WriteLine(reply.Data.ToString(true));
            Console.WriteLine();


            // Disconnect from the meter.
            client.DisconnectRequest();
            ReadDLMSPacket(client.DisconnectRequest(), reply, client, media);
        }
        finally
        {
            // Ensure the serial port is closed after communication.
            //ReadDLMSPacket(client.DisconnectRequest(), reply, client, media);
            media.Close();
        }

        Console.WriteLine("Communication completed successfully.");
    }

    static void InitSerial(IGXMedia media)
    {
        var initializeIEC = true; // Flag to determine if IEC initialization is needed.
        var waitTime = 9000; // Maximum wait time for receiving a response.

        GXSerial serial = media as GXSerial;
        byte Terminator = (byte)0x0A; // End-of-packet terminator for IEC communication.

        if (serial != null && initializeIEC)
        {
            // Set initial baud rate and communication parameters for IEC.
            serial.BaudRate = 300;
            serial.DataBits = 7;
            serial.Parity = Parity.Even;
            serial.StopBits = StopBits.One;
        }

        media.Open(); // Open the serial port.

        if (media != null && initializeIEC)
        {
            string data = "/?!\r\n"; // IEC initialization request.
            ReceiveParameters<string> p = new ReceiveParameters<string>()
            {
                Eop = Terminator, // Set end-of-packet terminator.
                WaitTime = waitTime // Set wait time for response.
            };

            lock (media.Synchronous)
            {
                media.Send(data, null); // Send IEC initialization request.

                if (!media.Receive(p))
                {
                    throw new Exception("Failed to receive reply from the device in given time.");
                }

                // Handle echo if present.
                if (p.Reply == data)
                {
                    p.Reply = null;
                    if (!media.Receive(p))
                    {
                        throw new Exception("Failed to receive reply from the device in given time.");
                    }
                }
            }

            Console.WriteLine("IEC Initialization Response: " + p.Reply);

            if (p.Reply[0] != '/')
            {
                throw new Exception("Invalid response during IEC initialization.");
            }

            char baudrate = p.Reply[4]; // Extract baud rate from response.
            int BaudRate = baudrate switch
            {
                '0' => 300,
                '1' => 600,
                '2' => 1200,
                '3' => 2400,
                '4' => 4800,
                '5' => 9600,
                '6' => 19200,
                _ => throw new Exception("Unknown baud rate.")
            };

            Console.WriteLine("BaudRate is: " + BaudRate);

            // Prepare command to switch to HDLC mode.
            byte controlCharacter = (byte)'2';
            byte ModeControlCharacter = (byte)'2';
            byte[] arr = [0x06, controlCharacter, (byte)baudrate, ModeControlCharacter, 13, 10];

            Console.WriteLine("Switching to HDLC mode: " + BitConverter.ToString(arr));
            media.Send(arr, null);
        }
    }

    public static void ReadDLMSPacket(byte[] data, GXReplyData reply, GXDLMSClient client, IGXMedia media)
    {
        var waitTime = 9000; // Maximum wait time for receiving a response.

        if (data == null)
        {
            return; // Exit if no data to send.
        }

        object eop = (byte)0x7E; // End-of-packet terminator for HDLC.

        if (client.InterfaceType == InterfaceType.WRAPPER)
        {
            eop = null; // No terminator for WRAPPER interface.
        }

        int retryCount = 0;
        bool succeeded = false;
        ReceiveParameters<byte[]> p = new ReceiveParameters<byte[]>()
        {
            AllData = true,
            Eop = eop,
            Count = 5,
            WaitTime = waitTime,
        };

        lock (media.Synchronous)
        {
            while (!succeeded && retryCount < 3)
            {
                media.Send(data, null); // Send data to the meter.
                succeeded = media.Receive(p); // Wait for response.

                if (!succeeded && ++retryCount == 3)
                {
                    throw new Exception("Failed to receive reply from the device in given time.");
                }
            }

            while (!client.GetData(p.Reply, reply))
            {
                if (!media.Receive(p))
                {
                    throw new Exception("Failed to receive reply from the device in given time.");
                }
            }
        }

        if (reply.Error != 0)
        {
            throw new GXDLMSException(reply.Error); // Handle DLMS-specific errors.
        }
    }
}

