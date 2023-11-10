#region Using directives
using System;
using UAManagedCore;
using OpcUa = UAManagedCore.OpcUa;
using FTOptix.OPCUAServer;
using FTOptix.HMIProject;
using FTOptix.UI;
using FTOptix.NativeUI;
using FTOptix.CoreBase;
using FTOptix.NetLogic;
using Barcoder;
using Barcoder.Qr;
using Barcoder.Renderer;
using Barcoder.Renderer.Image;
using System.IO;
using FTOptix.Core;
#endregion

public class QrBarCodeLogic : BaseNetLogic
{
    public override void Start()
    {
        // Insert code to be executed when the user-defined logic is started
    }

    public override void Stop()
    {
        // Insert code to be executed when the user-defined logic is stopped
    }

    [ExportMethod]
    public void CodeGenerator(string value, string type)
    {
        var renderer = new ImageRenderer(imageFormat: ImageFormat.Png);
        string filePath = new ResourceUri(LogicObject.Children.Get<IUAVariable>("FilePath").Value).Uri;
        switch (type)
        {
            case "QRCode":
                var barcode = QrEncoder.Encode(value, ErrorCorrectionLevel.L, Encoding.Auto);
                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    renderer.Render(barcode, stream);
                }
                break;
            case "Barcode39":
                barcode = Barcoder.Code39.Code39Encoder.Encode(value, false, false);
                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    renderer.Render(barcode, stream);
                }
                break;
            default:
                break;
        }
    }
}
