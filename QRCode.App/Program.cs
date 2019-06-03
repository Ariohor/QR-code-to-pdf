using System;
using System.Text;
using System.IO;
using iTextSharp.text;
using iTextSharp.text.pdf;
using System.Collections.Generic;
using QRCodeEncoderDecoderLibrary;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;

namespace QRCode.App
{
    class Program
    {   /// <summary>
        /// iTextSharp;                  - используем для того что бы поместить QR-Коды в PDF
        /// QRCodeEncoderDecoderLibrary; - используем для того что бы сгенерировать и прочитать QR-Коды original author is Uzi Granot
        /// </summary>
        /// <param name = "args" ></ param >
        static void Main(string[] args)
        {
            #region Xml To Base64

            var bytes = Encoding.UTF8.GetBytes(File.ReadAllText("XML.xml"));

            var stringBase64 = Convert.ToBase64String(bytes);
            #endregion

            #region Base64 To Substrings 1200 Lenth

            var substringsBase64 = new List<string>();
            var maximumLengthEncodingData = 1200; // L = 2953; Q = 1600; H = 1200 
            int startIndex = 0;
            for (; startIndex + maximumLengthEncodingData < stringBase64.Length;)
            {
                var newString = stringBase64.Substring(startIndex, maximumLengthEncodingData);
                var lenNewString = newString.Length;
                substringsBase64.Add(newString);

                startIndex += maximumLengthEncodingData;

            }

            substringsBase64.Add(stringBase64.Substring(startIndex));

            #endregion

            #region Substrings To QRCodes In PDF Collums (QRCodeEncoderDecoderLibrary + iTextSharp.text)

            Document document = new Document();
            using (var stream = new FileStream("test.pdf", FileMode.OpenOrCreate, FileAccess.Write, FileShare.ReadWrite))
            {
                const int optimalCountColumn = 2;
                PdfWriter.GetInstance(document, stream);

                document.Open();
                PdfPTable pdfTable = new PdfPTable(optimalCountColumn);

                foreach (var data in substringsBase64)
                {
                    QREncoder QRCodeEncoder = new QREncoder();

                    QRCodeEncoder.Encode(ErrorCorrection.H, data);

                    Bitmap QRCodeImage = QRCodeToBitmap.CreateBitmap(QRCodeEncoder, 4, 8);
                    var image = iTextSharp.text.Image.GetInstance(QRCodeImage, ImageFormat.Bmp);
                    image.ScalePercent(29);

                    PdfPCell cell = new PdfPCell(image);
                    cell.Padding = 1;
                    pdfTable.AddCell(cell);
                }

                var residue = substringsBase64.Count % optimalCountColumn;
                int countFreeCell = optimalCountColumn - residue;

                while (countFreeCell > 0)
                {
                    PdfPCell cell = new PdfPCell();
                    pdfTable.AddCell(cell);

                    countFreeCell--;
                }

                document.Add(pdfTable);
                document.Close();
            }

            #endregion

            #region Get List<> QRCodes From Image File  (QRCodeEncoderDecoderLibrary) PNG+ JPG-

            var qrDecoder = new QRDecoder();
            var listDataArray = new List<byte[][]>();

            var basicString = string.Empty;
            var basicBase64 = string.Empty;

            var imageNames = Directory.GetFiles(@"C:\Users\PBSOFT-2\Documents\Visual Studio 2015\Projects\Spire.BarcodeApp\Spire.BarcodeApp\bin\Debug", "*jpg").ToList();

            foreach (var imageName in imageNames)
            {
                using (var imageStream = new FileStream(imageName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    var bitmapImage = new Bitmap(imageStream);
                    var dataArray = qrDecoder.ImageDecoder(bitmapImage);
                    listDataArray.Add(dataArray);
                }
            }

            foreach (var arrays in listDataArray)
            {
                if (arrays != null)
                    foreach (var byteArray in arrays)
                    {
                        basicBase64 += QRCodeEncoderDecoderLibrary.QRCode.ByteArrayToStr(byteArray);
                    }
            }

            var fromBase64 = Convert.FromBase64String(basicBase64);

            basicString += Encoding.UTF8.GetString(fromBase64);

            #endregion
        }


    }
}