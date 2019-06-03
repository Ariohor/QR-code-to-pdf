﻿/////////////////////////////////////////////////////////////////////
//
//	QR Code Library
//
//	QR Code decoder.
//
//	Author: Uzi Granot
//	Version: 1.0
//	Date: June 30, 2018
//	Copyright (C) 2013-2018 Uzi Granot. All Rights Reserved
//
//	QR Code Library C# class library and the attached test/demo
//  applications are free software.
//	Software developed by this author is licensed under CPOL 1.02.
//	Some portions of the QRCodeVideoDecoder are licensed under GNU Lesser
//	General Public License v3.0.
//
//	The solution is made of 4 projects:
//	1. QRCodeEncoderDecoderLibrary: QR code encoding and decoding.
//	2. QRCodeEncoderDemo: Create QR Code images.
//	3. QRCodeDecoderDemo: Decode QR code image files.
//	4. QRCodeVideoDecoder: Decode QR code using web camera.
//		This demo program is using some of the source modules of
//		Camera_Net project published at CodeProject.com:
//		https://www.codeproject.com/Articles/671407/Camera_Net-Library
//		and at GitHub: https://github.com/free5lot/Camera_Net.
//		This project is based on DirectShowLib.
//		http://sourceforge.net/projects/directshownet/
//		This project includes a modified subset of the source modules.
//
//	The main points of CPOL 1.02 subject to the terms of the License are:
//
//	Source Code and Executable Files can be used in commercial applications;
//	Source Code and Executable Files can be redistributed; and
//	Source Code can be modified to create derivative works.
//	No claim of suitability, guarantee, or any warranty whatsoever is
//	provided. The software is provided "as-is".
//	The Article accompanying the Work may not be distributed or republished
//	without the Author's consent
//
//	For version history please refer to QRCode.cs
/////////////////////////////////////////////////////////////////////

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Text;
using System.Runtime.InteropServices;
using System.IO;
using System.Diagnostics;

namespace QRCodeEncoderDecoderLibrary
{
    public class QRDecoder : QRCode
    {
        internal int ImageWidth;
        internal int ImageHeight;
        internal bool[,] BlackWhiteImage;
        internal List<Finder> FinderList;
        internal List<Finder> AlignList;
        internal List<byte[]> DataArrayList;

        internal bool Trans4Mode;

        // transformation cooefficients from QR modules to image pixels
        internal double Trans3a;
        internal double Trans3b;
        internal double Trans3c;
        internal double Trans3d;
        internal double Trans3e;
        internal double Trans3f;

        // transformation matrix based on three finders plus one more point
        internal double Trans4a;
        internal double Trans4b;
        internal double Trans4c;
        internal double Trans4d;
        internal double Trans4e;
        internal double Trans4f;
        internal double Trans4g;
        internal double Trans4h;

        internal const double SIGNATURE_MAX_DEVIATION = 0.25;
        internal const double HOR_VERT_SCAN_MAX_DISTANCE = 2.0;
        internal const double MODULE_SIZE_DEVIATION = 0.5; // 0.75;
        internal const double CORNER_SIDE_LENGTH_DEV = 0.8;
        internal const double CORNER_RIGHT_ANGLE_DEV = 0.25; // about Sin(4 deg)
        internal const double ALIGNMENT_SEARCH_AREA = 0.3;

        ////////////////////////////////////////////////////////////////////
        // constructors
        ////////////////////////////////////////////////////////////////////

        public QRDecoder() { }

        ////////////////////////////////////////////////////////////////////
        // Decode QRCode boolean matrix
        ////////////////////////////////////////////////////////////////////

        public byte[][] ImageDecoder
                (
                Bitmap InputImage
                )
        {
#if DEBUG
            int Start;
#endif
            try
            {
                // empty data string output
                DataArrayList = new List<byte[]>();

                // save image dimension
                ImageWidth = InputImage.Width;
                ImageHeight = InputImage.Height;

#if DEBUG
                Start = Environment.TickCount;
                QRCodeTrace.Write("Convert image to black and white");
#endif

                // convert input image to black and white boolean image
                if (!ConvertImageToBlackAndWhite(InputImage)) return null;

#if DEBUG
                QRCodeTrace.Format("Time: {0}", Environment.TickCount - Start);
                QRCodeTrace.Write("Finders search");
#endif

                // horizontal search for finders
                if (!HorizontalFindersSearch()) return null;

#if DEBUG
                QRCodeTrace.Format("Horizontal Finders count: {0}", FinderList.Count);
#endif

                // vertical search for finders
                VerticalFindersSearch();

#if DEBUG
                int MatchedCount = 0;
                foreach (Finder HF in FinderList) if (HF.Distance != double.MaxValue) MatchedCount++;
                QRCodeTrace.Format("Matched Finders count: {0}", MatchedCount);
                QRCodeTrace.Write("Remove all unused finders");
#endif

                // remove unused finders
                if (!RemoveUnusedFinders()) return null;

#if DEBUG
                QRCodeTrace.Format("Time: {0}", Environment.TickCount - Start);
                foreach (Finder HF in FinderList) QRCodeTrace.Write(HF.ToString());
                QRCodeTrace.Write("Search for QR corners");
#endif
            }

#if DEBUG
            catch (Exception Ex)
            {
                QRCodeTrace.Write("QR Code decoding failed (no finders). " + Ex.Message);
                return null;
            }
#else
		catch
			{
			return null;
			}
#endif

            // look for all possible 3 finder patterns
            int Index1End = FinderList.Count - 2;
            int Index2End = FinderList.Count - 1;
            int Index3End = FinderList.Count;
            for (int Index1 = 0; Index1 < Index1End; Index1++)
                for (int Index2 = Index1 + 1; Index2 < Index2End; Index2++)
                    for (int Index3 = Index2 + 1; Index3 < Index3End; Index3++)
                    {
                        try
                        {
                            // find 3 finders arranged in L shape
                            Corner Corner = Corner.CreateCorner(FinderList[Index1], FinderList[Index2], FinderList[Index3]);

                            // not a valid corner
                            if (Corner == null) continue;

#if DEBUG
                            QRCodeTrace.Format("Decode Corner: Top Left:    {0}", Corner.TopLeftFinder.ToString());
                            QRCodeTrace.Format("Decode Corner: Top Right:   {0}", Corner.TopRightFinder.ToString());
                            QRCodeTrace.Format("Decode Corner: Bottom Left: {0}", Corner.BottomLeftFinder.ToString());
#endif

                            // get corner info (version, error code and mask)
                            // continue if failed
                            if (!GetQRCodeCornerInfo(Corner)) continue;

#if DEBUG
                            QRCodeTrace.Write("Decode QR code using three finders");
#endif

                            // decode corner using three finders
                            // continue if successful
                            if (DecodeQRCodeCorner(Corner)) continue;

                            // qr code version 1 has no alignment mark
                            // in other words decode failed 
                            if (QRCodeVersion == 1) continue;

                            // find bottom right alignment mark
                            // continue if failed
                            if (!FindAlignmentMark(Corner)) continue;

                            // decode using 4 points
                            foreach (Finder Align in AlignList)
                            {
#if DEBUG
                                QRCodeTrace.Format("Calculated alignment mark: Row {0}, Col {1}", Align.Row, Align.Col);
#endif

                                // calculate transformation based on 3 finders and bottom right alignment mark
                                SetTransMatrix(Corner, Align.Row, Align.Col);

                                // decode corner using three finders and one alignment mark
                                if (DecodeQRCodeCorner(Corner)) break;
                            }
                        }

#if DEBUG
                        catch (Exception Ex)
                        {
                            QRCodeTrace.Write("Decode corner failed. " + Ex.Message);
                            continue;
                        }
#else
			catch
				{
				continue;
				}
#endif
                    }

#if DEBUG
            QRCodeTrace.Format("Time: {0}", Environment.TickCount - Start);
#endif

            // not found exit
            if (DataArrayList.Count == 0)
            {
#if DEBUG
                QRCodeTrace.Write("No QR Code found");
#endif
                return null;
            }

            // successful exit
            return DataArrayList.ToArray();
        }

        /// <summary>
        /// Format result for display
        /// </summary>
        /// <param name="DataByteArray"></param>
        /// <returns></returns>
        public static string QRCodeResult
                (
                byte[][] DataByteArray
                )
        {
            // no QR code
            if (DataByteArray == null) return string.Empty;

            // image has one QR code
            if (DataByteArray.Length == 1) return ByteArrayToStr(DataByteArray[0]);

            // image has more than one QR code
            StringBuilder Str = new StringBuilder();
            for (int Index = 0; Index < DataByteArray.Length; Index++)
            {
                if (Index != 0) Str.Append("\r\n");
                Str.AppendFormat("QR Code {0}\r\n", Index + 1);
                Str.Append(ByteArrayToStr(DataByteArray[Index]));
            }
            return Str.ToString();
        }

        ////////////////////////////////////////////////////////////////////
        // Convert image to black and white boolean matrix
        ////////////////////////////////////////////////////////////////////

        internal bool ConvertImageToBlackAndWhite
                (
                Bitmap InputImage
                )
        {
            // lock image bits
            BitmapData BitmapData = InputImage.LockBits(new Rectangle(0, 0, ImageWidth, ImageHeight),
                ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);

            // address of first line
            IntPtr BitArrayPtr = BitmapData.Scan0;

            // length in bytes of one scan line
            int ScanLineWidth = BitmapData.Stride;
            if (ScanLineWidth < 0)
            {
#if DEBUG
                QRCodeTrace.Write("Convert image to back and white array. Invalid input image format (upside down).");
#endif
                return false;
            }

            // image total bytes
            int TotalBytes = ScanLineWidth * ImageHeight;
            byte[] BitmapArray = new byte[TotalBytes];

            // Copy the RGB values into the array.
            Marshal.Copy(BitArrayPtr, BitmapArray, 0, TotalBytes);

            // unlock image
            InputImage.UnlockBits(BitmapData);

            // allocate gray image 
            byte[,] GrayImage = new byte[ImageHeight, ImageWidth];
            int[] GrayLevel = new int[256];

            // convert to gray
            int Delta = ScanLineWidth - 3 * ImageWidth;
            int BitmapPtr = 0;
            for (int Row = 0; Row < ImageHeight; Row++)
            {
                for (int Col = 0; Col < ImageWidth; Col++)
                {
                    int Module = (30 * BitmapArray[BitmapPtr] + 59 * BitmapArray[BitmapPtr + 1] + 11 * BitmapArray[BitmapPtr + 2]) / 100;
                    GrayLevel[Module]++;
                    GrayImage[Row, Col] = (byte)Module;
                    BitmapPtr += 3;
                }
                BitmapPtr += Delta;
            }

            // gray level cutoff between black and white
            int LevelStart;
            int LevelEnd;
            for (LevelStart = 0; LevelStart < 256 && GrayLevel[LevelStart] == 0; LevelStart++) ;
            for (LevelEnd = 255; LevelEnd >= LevelStart && GrayLevel[LevelEnd] == 0; LevelEnd--) ;
            LevelEnd++;
            if (LevelEnd - LevelStart < 2)
            {
#if DEBUG
                QRCodeTrace.Write("Convert image to back and white array. Input image has no color variations");
#endif
                return false;
            }

            int CutoffLevel = (LevelStart + LevelEnd) / 2;

            // create boolean image white = false, black = true
            BlackWhiteImage = new bool[ImageHeight, ImageWidth];
            for (int Row = 0; Row < ImageHeight; Row++)
                for (int Col = 0; Col < ImageWidth; Col++)
                    BlackWhiteImage[Row, Col] = GrayImage[Row, Col] < CutoffLevel;

            // save as black white image
#if DEBUGEX
		QRCodeTrace.Write("Display black and white image");
		DisplayBlackAndWhiteImage();
#endif

            // exit;
            return true;
        }

        ////////////////////////////////////////////////////////////////////
        // Save and display black and white boolean image as png image
        ////////////////////////////////////////////////////////////////////

#if DEBUGX
	internal void DisplayBlackAndWhiteImage()
		{
		int ModuleSize = Math.Min(16384 / Math.Max(ImageHeight, ImageWidth), 1);
		SolidBrush BrushWhite = new SolidBrush(Color.White);
		SolidBrush BrushBlack = new SolidBrush(Color.Black);
		Bitmap Image = new Bitmap(ImageWidth * ModuleSize, ImageHeight * ModuleSize);
		Graphics Graphics = Graphics.FromImage(Image);
		Graphics.FillRectangle(BrushWhite, 0, 0, ImageWidth * ModuleSize, ImageHeight * ModuleSize);
		for(int Row = 0; Row < ImageHeight; Row++) for(int Col = 0; Col < ImageWidth; Col++)
			{
			if(BlackWhiteImage[Row, Col]) Graphics.FillRectangle(BrushBlack, Col * ModuleSize, Row * ModuleSize, ModuleSize, ModuleSize);
			}
		string FileName = "DecodeImage.png";
		try
			{
			FileStream fs = new FileStream(FileName, FileMode.Create);
			Image.Save(fs, ImageFormat.Png);
			fs.Close();
			}
		catch(IOException)
			{
			FileName = null;
			}

		// start image editor
		if(FileName != null) Process.Start(FileName);
		return;
		}
#endif

        ////////////////////////////////////////////////////////////////////
        // search row by row for finders blocks
        ////////////////////////////////////////////////////////////////////

        internal bool HorizontalFindersSearch()
        {
            // create empty finders list
            FinderList = new List<Finder>();

            // look for finder patterns
            int[] ColPos = new int[ImageWidth + 1];
            int PosPtr = 0;

            // scan one row at a time
            for (int Row = 0; Row < ImageHeight; Row++)
            {
                // look for first black pixel
                int Col;
                for (Col = 0; Col < ImageWidth && !BlackWhiteImage[Row, Col]; Col++) ;
                if (Col == ImageWidth) continue;

                // first black
                PosPtr = 0;
                ColPos[PosPtr++] = Col;

                // loop for pairs
                for (;;)
                {
                    // look for next white
                    // if black is all the way to the edge, set next white after the edge
                    for (; Col < ImageWidth && BlackWhiteImage[Row, Col]; Col++) ;
                    ColPos[PosPtr++] = Col;
                    if (Col == ImageWidth) break;

                    // look for next black
                    for (; Col < ImageWidth && !BlackWhiteImage[Row, Col]; Col++) ;
                    if (Col == ImageWidth) break;
                    ColPos[PosPtr++] = Col;
                }

                // we must have at least 6 positions
                if (PosPtr < 6) continue;

                // build length array
                int PosLen = PosPtr - 1;
                int[] Len = new int[PosLen];
                for (int Ptr = 0; Ptr < PosLen; Ptr++) Len[Ptr] = ColPos[Ptr + 1] - ColPos[Ptr];

                // test signature
                int SigLen = PosPtr - 5;
                for (int SigPtr = 0; SigPtr < SigLen; SigPtr += 2)
                {
                    double ModuleSize;
                    if (TestFinderSig(ColPos, Len, SigPtr, out ModuleSize))
                        FinderList.Add(new Finder(Row, ColPos[SigPtr + 2], ColPos[SigPtr + 3], ModuleSize));
                }
            }

            // no finders found
            if (FinderList.Count < 3)
            {
#if DEBUG
                QRCodeTrace.Write("Horizontal finders search. Less than 3 finders found");
#endif
                return false;
            }

            // exit
            return true;
        }

        ////////////////////////////////////////////////////////////////////
        // search row by row for alignment blocks
        ////////////////////////////////////////////////////////////////////

        internal bool HorizontalAlignmentSearch
                (
                int AreaLeft,
                int AreaTop,
                int AreaWidth,
                int AreaHeight
                )
        {
            // create empty finders list
            AlignList = new List<Finder>();

            // look for finder patterns
            int[] ColPos = new int[AreaWidth + 1];
            int PosPtr = 0;

            // area right and bottom
            int AreaRight = AreaLeft + AreaWidth;
            int AreaBottom = AreaTop + AreaHeight;

            // scan one row at a time
            for (int Row = AreaTop; Row < AreaBottom; Row++)
            {
                // look for first black pixel
                int Col;
                for (Col = AreaLeft; Col < AreaRight && !BlackWhiteImage[Row, Col]; Col++) ;
                if (Col == AreaRight) continue;

                // first black
                PosPtr = 0;
                ColPos[PosPtr++] = Col;

                // loop for pairs
                for (;;)
                {
                    // look for next white
                    // if black is all the way to the edge, set next white after the edge
                    for (; Col < AreaRight && BlackWhiteImage[Row, Col]; Col++) ;
                    ColPos[PosPtr++] = Col;
                    if (Col == AreaRight) break;

                    // look for next black
                    for (; Col < AreaRight && !BlackWhiteImage[Row, Col]; Col++) ;
                    if (Col == AreaRight) break;
                    ColPos[PosPtr++] = Col;
                }

                // we must have at least 6 positions
                if (PosPtr < 6) continue;

                // build length array
                int PosLen = PosPtr - 1;
                int[] Len = new int[PosLen];
                for (int Ptr = 0; Ptr < PosLen; Ptr++) Len[Ptr] = ColPos[Ptr + 1] - ColPos[Ptr];

                // test signature
                int SigLen = PosPtr - 5;
                for (int SigPtr = 0; SigPtr < SigLen; SigPtr += 2)
                {
                    double ModuleSize;
                    if (TestAlignSig(ColPos, Len, SigPtr, out ModuleSize))
                        AlignList.Add(new Finder(Row, ColPos[SigPtr + 2], ColPos[SigPtr + 3], ModuleSize));
                }
            }

            // list is now empty or has less than three finders
#if DEBUG
            if (AlignList.Count == 0) QRCodeTrace.Write("Vertical align search.\r\nNo finders found");
#endif

            // exit
            return AlignList.Count != 0;
        }

        ////////////////////////////////////////////////////////////////////
        // search column by column for finders blocks
        ////////////////////////////////////////////////////////////////////

        internal void VerticalFindersSearch()
        {
            // active columns
            bool[] ActiveColumn = new bool[ImageWidth];
            foreach (Finder HF in FinderList)
            {
                for (int Col = HF.Col1; Col < HF.Col2; Col++) ActiveColumn[Col] = true;
            }

            // look for finder patterns
            int[] RowPos = new int[ImageHeight + 1];
            int PosPtr = 0;

            // scan one column at a time
            for (int Col = 0; Col < ImageWidth; Col++)
            {
                // not active column
                if (!ActiveColumn[Col]) continue;

                // look for first black pixel
                int Row;
                for (Row = 0; Row < ImageHeight && !BlackWhiteImage[Row, Col]; Row++) ;
                if (Row == ImageWidth) continue;

                // first black
                PosPtr = 0;
                RowPos[PosPtr++] = Row;

                // loop for pairs
                for (;;)
                {
                    // look for next white
                    // if black is all the way to the edge, set next white after the edge
                    for (; Row < ImageHeight && BlackWhiteImage[Row, Col]; Row++) ;
                    RowPos[PosPtr++] = Row;
                    if (Row == ImageHeight) break;

                    // look for next black
                    for (; Row < ImageHeight && !BlackWhiteImage[Row, Col]; Row++) ;
                    if (Row == ImageHeight) break;
                    RowPos[PosPtr++] = Row;
                }

                // we must have at least 6 positions
                if (PosPtr < 6) continue;

                // build length array
                int PosLen = PosPtr - 1;
                int[] Len = new int[PosLen];
                for (int Ptr = 0; Ptr < PosLen; Ptr++) Len[Ptr] = RowPos[Ptr + 1] - RowPos[Ptr];

                // test signature
                int SigLen = PosPtr - 5;
                for (int SigPtr = 0; SigPtr < SigLen; SigPtr += 2)
                {
                    double ModuleSize;
                    if (!TestFinderSig(RowPos, Len, SigPtr, out ModuleSize)) continue;
                    foreach (Finder HF in FinderList)
                    {
                        HF.Match(Col, RowPos[SigPtr + 2], RowPos[SigPtr + 3], ModuleSize);
                    }
                }
            }

            // exit
            return;
        }

        ////////////////////////////////////////////////////////////////////
        // search column by column for finders blocks
        ////////////////////////////////////////////////////////////////////

        internal void VerticalAlignmentSearch
                (
                int AreaLeft,
                int AreaTop,
                int AreaWidth,
                int AreaHeight
                )
        {
            // active columns
            bool[] ActiveColumn = new bool[AreaWidth];
            foreach (Finder HF in AlignList)
            {
                for (int Col = HF.Col1; Col < HF.Col2; Col++) ActiveColumn[Col - AreaLeft] = true;
            }

            // look for finder patterns
            int[] RowPos = new int[AreaHeight + 1];
            int PosPtr = 0;

            // area right and bottom
            int AreaRight = AreaLeft + AreaWidth;
            int AreaBottom = AreaTop + AreaHeight;

            // scan one column at a time
            for (int Col = AreaLeft; Col < AreaRight; Col++)
            {
                // not active column
                if (!ActiveColumn[Col - AreaLeft]) continue;

                // look for first black pixel
                int Row;
                for (Row = AreaTop; Row < AreaBottom && !BlackWhiteImage[Row, Col]; Row++) ;
                if (Row == AreaBottom) continue;

                // first black
                PosPtr = 0;
                RowPos[PosPtr++] = Row;

                // loop for pairs
                for (;;)
                {
                    // look for next white
                    // if black is all the way to the edge, set next white after the edge
                    for (; Row < AreaBottom && BlackWhiteImage[Row, Col]; Row++) ;
                    RowPos[PosPtr++] = Row;
                    if (Row == AreaBottom) break;

                    // look for next black
                    for (; Row < AreaBottom && !BlackWhiteImage[Row, Col]; Row++) ;
                    if (Row == AreaBottom) break;
                    RowPos[PosPtr++] = Row;
                }

                // we must have at least 6 positions
                if (PosPtr < 6) continue;

                // build length array
                int PosLen = PosPtr - 1;
                int[] Len = new int[PosLen];
                for (int Ptr = 0; Ptr < PosLen; Ptr++) Len[Ptr] = RowPos[Ptr + 1] - RowPos[Ptr];

                // test signature
                int SigLen = PosPtr - 5;
                for (int SigPtr = 0; SigPtr < SigLen; SigPtr += 2)
                {
                    double ModuleSize;
                    if (!TestAlignSig(RowPos, Len, SigPtr, out ModuleSize)) continue;
                    foreach (Finder HF in AlignList)
                    {
                        HF.Match(Col, RowPos[SigPtr + 2], RowPos[SigPtr + 3], ModuleSize);
                    }
                }
            }

            // exit
            return;
        }

        ////////////////////////////////////////////////////////////////////
        // search column by column for finders blocks
        ////////////////////////////////////////////////////////////////////

        internal bool RemoveUnusedFinders()
        {
            // remove all entries without a match
            for (int Index = 0; Index < FinderList.Count; Index++)
            {
                if (FinderList[Index].Distance == double.MaxValue)
                {
                    FinderList.RemoveAt(Index);
                    Index--;
                }
            }

            // list is now empty or has less than three finders
            if (FinderList.Count < 3)
            {
#if DEBUG
                QRCodeTrace.Write("Remove unmatched finders. Less than 3 finders found");
#endif
                return false;
            }

            // keep best entry for each overlapping area
            for (int Index = 0; Index < FinderList.Count; Index++)
            {
                Finder Finder = FinderList[Index];
                for (int Index1 = Index + 1; Index1 < FinderList.Count; Index1++)
                {
                    Finder Finder1 = FinderList[Index1];
                    if (!Finder.Overlap(Finder1)) continue;
                    if (Finder1.Distance < Finder.Distance)
                    {
                        Finder = Finder1;
                        FinderList[Index] = Finder;
                    }
                    FinderList.RemoveAt(Index1);
                    Index1--;
                }
            }

            // list is now empty or has less than three finders
            if (FinderList.Count < 3)
            {
#if DEBUG
                QRCodeTrace.Write("Keep best matched finders. Less than 3 finders found");
#endif
                return false;
            }

            // exit
            return true;
        }

        ////////////////////////////////////////////////////////////////////
        // search column by column for finders blocks
        ////////////////////////////////////////////////////////////////////

        internal bool RemoveUnusedAlignMarks()
        {
            // remove all entries without a match
            for (int Index = 0; Index < AlignList.Count; Index++)
            {
                if (AlignList[Index].Distance == double.MaxValue)
                {
                    AlignList.RemoveAt(Index);
                    Index--;
                }
            }

            // keep best entry for each overlapping area
            for (int Index = 0; Index < AlignList.Count; Index++)
            {
                Finder Finder = AlignList[Index];
                for (int Index1 = Index + 1; Index1 < AlignList.Count; Index1++)
                {
                    Finder Finder1 = AlignList[Index1];
                    if (!Finder.Overlap(Finder1)) continue;
                    if (Finder1.Distance < Finder.Distance)
                    {
                        Finder = Finder1;
                        AlignList[Index] = Finder;
                    }
                    AlignList.RemoveAt(Index1);
                    Index1--;
                }
            }

            // list is now empty or has less than three finders
#if DEBUG
            if (AlignList.Count == 0)
                QRCodeTrace.Write("Remove unused alignment marks.\r\nNo alignment marks found");
#endif

            // exit
            return AlignList.Count != 0;
        }

        ////////////////////////////////////////////////////////////////////
        // test finder signature 1 1 3 1 1
        ////////////////////////////////////////////////////////////////////

        internal bool TestFinderSig
                (
                int[] Pos,
                int[] Len,
                int Index,
                out double Module
                )
        {
            Module = (Pos[Index + 5] - Pos[Index]) / 7.0;
            double MaxDev = SIGNATURE_MAX_DEVIATION * Module;
            if (Math.Abs(Len[Index] - Module) > MaxDev) return false;
            if (Math.Abs(Len[Index + 1] - Module) > MaxDev) return false;
            if (Math.Abs(Len[Index + 2] - 3 * Module) > MaxDev) return false;
            if (Math.Abs(Len[Index + 3] - Module) > MaxDev) return false;
            if (Math.Abs(Len[Index + 4] - Module) > MaxDev) return false;
            return true;
        }

        ////////////////////////////////////////////////////////////////////
        // test alignment signature n 1 1 1 n
        ////////////////////////////////////////////////////////////////////

        internal bool TestAlignSig
                (
                int[] Pos,
                int[] Len,
                int Index,
                out double Module
                )
        {
            Module = (Pos[Index + 4] - Pos[Index + 1]) / 3.0;
            double MaxDev = SIGNATURE_MAX_DEVIATION * Module;
            if (Len[Index] < Module - MaxDev) return false;
            if (Math.Abs(Len[Index + 1] - Module) > MaxDev) return false;
            if (Math.Abs(Len[Index + 2] - Module) > MaxDev) return false;
            if (Math.Abs(Len[Index + 3] - Module) > MaxDev) return false;
            if (Len[Index + 4] < Module - MaxDev) return false;
            return true;
        }

        ////////////////////////////////////////////////////////////////////
        // Build corner list
        ////////////////////////////////////////////////////////////////////

        internal List<Corner> BuildCornerList()
        {
            // empty list
            List<Corner> Corners = new List<Corner>();

            // look for all possible 3 finder patterns
            int Index1End = FinderList.Count - 2;
            int Index2End = FinderList.Count - 1;
            int Index3End = FinderList.Count;
            for (int Index1 = 0; Index1 < Index1End; Index1++)
                for (int Index2 = Index1 + 1; Index2 < Index2End; Index2++)
                    for (int Index3 = Index2 + 1; Index3 < Index3End; Index3++)
                    {
                        // find 3 finders arranged in L shape
                        Corner Corner = Corner.CreateCorner(FinderList[Index1], FinderList[Index2], FinderList[Index3]);

                        // add corner to list
                        if (Corner != null) Corners.Add(Corner);
                    }

            // exit
            return Corners.Count == 0 ? null : Corners;
        }

        ////////////////////////////////////////////////////////////////////
        // Get QR Code corner info
        ////////////////////////////////////////////////////////////////////

        internal bool GetQRCodeCornerInfo
                (
                Corner Corner
                )
        {
            try
            {
                // initial version number
                QRCodeVersion = Corner.InitialVersionNumber();

                // qr code dimension
                QRCodeDimension = 17 + 4 * QRCodeVersion;

#if DEBUG
                QRCodeTrace.Format("Initial version number: {0}, dimension: {1}", QRCodeVersion, QRCodeDimension);
#endif

                // set transformation matrix
                SetTransMatrix(Corner);

                // if version number is 7 or more, get version code
                if (QRCodeVersion >= 7)
                {
                    int Version = GetVersionOne();
                    if (Version == 0)
                    {
                        Version = GetVersionTwo();
                        if (Version == 0) return false;
                    }

                    // QR Code version number is different than initial version
                    if (Version != QRCodeVersion)
                    {
                        // initial version number and dimension
                        QRCodeVersion = Version;

                        // qr code dimension
                        QRCodeDimension = 17 + 4 * QRCodeVersion;

#if DEBUG
                        QRCodeTrace.Format("Updated version number: {0}, dimension: {1}", QRCodeVersion, QRCodeDimension);
#endif

                        // set transformation matrix
                        SetTransMatrix(Corner);
                    }
                }

                // get format info arrays
                int FormatInfo = GetFormatInfoOne();
                if (FormatInfo < 0)
                {
                    FormatInfo = GetFormatInfoTwo();
                    if (FormatInfo < 0) return false;
                }

                // set error correction code and mask code
                ErrorCorrection = FormatInfoToErrCode(FormatInfo >> 3);
                MaskCode = FormatInfo & 7;

                // successful exit
                return true;
            }

#if DEBUG
            catch (Exception Ex)
            {
                QRCodeTrace.Format("Get QR Code corner info exception.\r\n{0}", Ex.Message);

                // failed exit
                return false;
            }
#else
		catch
			{
			// failed exit
			return false;
			}
#endif
        }

        ////////////////////////////////////////////////////////////////////
        // Search for QR Code version
        ////////////////////////////////////////////////////////////////////

        internal bool DecodeQRCodeCorner
                (
                Corner Corner
                )
        {
            try
            {
                // create base matrix
                BuildBaseMatrix();

                // create data matrix and test fixed modules
                ConvertImageToMatrix();

                // based on version and format information
                // set number of data and error correction codewords length  
                SetDataCodewordsLength();

                // apply mask as per get format information step
                ApplyMask(MaskCode);

                // unload data from binary matrix to byte format
                UnloadDataFromMatrix();

                // restore blocks (undo interleave)
                RestoreBlocks();

                // calculate error correction
                // in case of error try to correct it
                CalculateErrorCorrection();

                // decode data
                byte[] DataArray = DecodeData();
                DataArrayList.Add(DataArray);

                // trace
#if DEBUG
                string DataString = ByteArrayToStr(DataArray);
                QRCodeTrace.Format("Version: {0}, Dim: {1}, ErrCorr: {2}, Generator: {3}, Mask: {4}, Group1: {5}:{6}, Group2: {7}:{8}",
                    QRCodeVersion.ToString(), QRCodeDimension.ToString(), ErrorCorrection.ToString(), ErrCorrCodewords.ToString(), MaskCode.ToString(),
                    BlocksGroup1.ToString(), DataCodewordsGroup1.ToString(), BlocksGroup2.ToString(), DataCodewordsGroup2.ToString());
                QRCodeTrace.Format("Data: {0}", DataString);
#endif

                // successful exit
                return true;
            }

#if DEBUG
            catch (Exception Ex)
            {
                QRCodeTrace.Format("Decode QR code exception.\r\n{0}", Ex.Message);

                // failed exit
                return false;
            }
#else
		catch
			{
			// failed exit
			return false;
			}
#endif
        }

        internal void SetTransMatrix
                (
                Corner Corner
                )
        {
            // save
            int BottomRightPos = QRCodeDimension - 4;

            // transformation matrix based on three finders
            double[,] Matrix1 = new double[3, 4];
            double[,] Matrix2 = new double[3, 4];

            // build matrix 1 for horizontal X direction
            Matrix1[0, 0] = 3;
            Matrix1[0, 1] = 3;
            Matrix1[0, 2] = 1;
            Matrix1[0, 3] = Corner.TopLeftFinder.Col;

            Matrix1[1, 0] = BottomRightPos;
            Matrix1[1, 1] = 3;
            Matrix1[1, 2] = 1;
            Matrix1[1, 3] = Corner.TopRightFinder.Col;

            Matrix1[2, 0] = 3;
            Matrix1[2, 1] = BottomRightPos;
            Matrix1[2, 2] = 1;
            Matrix1[2, 3] = Corner.BottomLeftFinder.Col;

            // build matrix 2 for Vertical Y direction
            Matrix2[0, 0] = 3;
            Matrix2[0, 1] = 3;
            Matrix2[0, 2] = 1;
            Matrix2[0, 3] = Corner.TopLeftFinder.Row;

            Matrix2[1, 0] = BottomRightPos;
            Matrix2[1, 1] = 3;
            Matrix2[1, 2] = 1;
            Matrix2[1, 3] = Corner.TopRightFinder.Row;

            Matrix2[2, 0] = 3;
            Matrix2[2, 1] = BottomRightPos;
            Matrix2[2, 2] = 1;
            Matrix2[2, 3] = Corner.BottomLeftFinder.Row;

            // solve matrix1
            SolveMatrixOne(Matrix1);
            Trans3a = Matrix1[0, 3];
            Trans3c = Matrix1[1, 3];
            Trans3e = Matrix1[2, 3];

            // solve matrix2
            SolveMatrixOne(Matrix2);
            Trans3b = Matrix2[0, 3];
            Trans3d = Matrix2[1, 3];
            Trans3f = Matrix2[2, 3];

            // reset trans 4 mode
            Trans4Mode = false;
            return;
        }

        internal void SolveMatrixOne
                (
                double[,] Matrix
                )
        {
            for (int Row = 0; Row < 3; Row++)
            {
                // If the element is zero, make it non zero by adding another row
                if (Matrix[Row, Row] == 0)
                {
                    int Row1;
                    for (Row1 = Row + 1; Row1 < 3 && Matrix[Row1, Row] == 0; Row1++) ;
                    if (Row1 == 3) throw new ApplicationException("Solve linear equations failed");

                    for (int Col = Row; Col < 4; Col++) Matrix[Row, Col] += Matrix[Row1, Col];
                }

                // make the diagonal element 1.0
                for (int Col = 3; Col > Row; Col--) Matrix[Row, Col] /= Matrix[Row, Row];

                // subtract current row from next rows to eliminate one value
                for (int Row1 = Row + 1; Row1 < 3; Row1++)
                {
                    for (int Col = 3; Col > Row; Col--) Matrix[Row1, Col] -= Matrix[Row, Col] * Matrix[Row1, Row];
                }
            }

            // go up from last row and eliminate all solved values
            Matrix[1, 3] -= Matrix[1, 2] * Matrix[2, 3];
            Matrix[0, 3] -= Matrix[0, 2] * Matrix[2, 3];
            Matrix[0, 3] -= Matrix[0, 1] * Matrix[1, 3];
            return;
        }

        ////////////////////////////////////////////////////////////////////
        // Get image pixel color
        ////////////////////////////////////////////////////////////////////

        internal bool GetModule
                (
                int Row,
                int Col
                )
        {
            // get module based on three finders
            if (!Trans4Mode)
            {
                int Trans3Col = (int)Math.Round(Trans3a * Col + Trans3c * Row + Trans3e, 0, MidpointRounding.AwayFromZero);
                int Trans3Row = (int)Math.Round(Trans3b * Col + Trans3d * Row + Trans3f, 0, MidpointRounding.AwayFromZero);
                return BlackWhiteImage[Trans3Row, Trans3Col];
            }

            // get module based on three finders plus one alignment mark
            double W = Trans4g * Col + Trans4h * Row + 1.0;
            int Trans4Col = (int)Math.Round((Trans4a * Col + Trans4b * Row + Trans4c) / W, 0, MidpointRounding.AwayFromZero);
            int Trans4Row = (int)Math.Round((Trans4d * Col + Trans4e * Row + Trans4f) / W, 0, MidpointRounding.AwayFromZero);
            return BlackWhiteImage[Trans4Row, Trans4Col];
        }

        ////////////////////////////////////////////////////////////////////
        // search row by row for finders blocks
        ////////////////////////////////////////////////////////////////////

        internal bool FindAlignmentMark
                (
                Corner Corner
                )
        {
            // alignment mark estimated position
            int AlignRow = QRCodeDimension - 7;
            int AlignCol = QRCodeDimension - 7;
            int ImageCol = (int)Math.Round(Trans3a * AlignCol + Trans3c * AlignRow + Trans3e, 0, MidpointRounding.AwayFromZero);
            int ImageRow = (int)Math.Round(Trans3b * AlignCol + Trans3d * AlignRow + Trans3f, 0, MidpointRounding.AwayFromZero);

#if DEBUG
            QRCodeTrace.Format("Estimated alignment mark: Row {0}, Col {1}", ImageRow, ImageCol);
#endif

            // search area
            int Side = (int)Math.Round(ALIGNMENT_SEARCH_AREA * (Corner.TopLineLength + Corner.LeftLineLength), 0, MidpointRounding.AwayFromZero);

            int AreaLeft = ImageCol - Side / 2;
            int AreaTop = ImageRow - Side / 2;
            int AreaWidth = Side;
            int AreaHeight = Side;

#if DEBUGEX
		DisplayBottomRightCorder(AreaLeft, AreaTop, AreaWidth, AreaHeight);
#endif

            // horizontal search for finders
            if (!HorizontalAlignmentSearch(AreaLeft, AreaTop, AreaWidth, AreaHeight)) return false;

            // vertical search for finders
            VerticalAlignmentSearch(AreaLeft, AreaTop, AreaWidth, AreaHeight);

            // remove unused alignment entries
            if (!RemoveUnusedAlignMarks()) return false;

            // successful exit
            return true;
        }

        internal void DisplayBottomRightCorder
                (
                int AreaLeft,
                int AreaTop,
                int AreaWidth,
                int AreaHeight
                )
        {
            SolidBrush BrushWhite = new SolidBrush(Color.White);
            SolidBrush BrushBlack = new SolidBrush(Color.Black);
            const int Module = 4;

            // create picture object and make it white
            Bitmap ImageBitmap = new Bitmap(Module * AreaWidth, Module * AreaHeight);
            Graphics Graphics = Graphics.FromImage(ImageBitmap);
            Graphics.FillRectangle(BrushWhite, 0, 0, Module * AreaWidth, Module * AreaHeight);

            // shortcut
            int PosX = 0;
            int PosY = 0;

            // paint QR Code image
            for (int Row = 0; Row < AreaHeight; Row++)
            {
                for (int Col = 0; Col < AreaWidth; Col++)
                {
                    if (BlackWhiteImage[AreaTop + Row, AreaLeft + Col]) Graphics.FillRectangle(BrushBlack, PosX, PosY, Module, Module);
                    PosX += Module;
                }
                PosX = 0;
                PosY += Module;
            }

            string FileName = "AlignImage.png";
            try
            {
                FileStream fs = new FileStream(FileName, FileMode.Create);
                ImageBitmap.Save(fs, ImageFormat.Png);
                fs.Close();
            }
            catch (IOException)
            {
                FileName = null;
            }

            // start image editor
            if (FileName != null) Process.Start(FileName);
            return;
        }

        internal void SetTransMatrix
                (
                Corner Corner,
                double ImageAlignRow,
                double ImageAlignCol
                )
        {
            // top right and bottom left QR code position
            int FarFinder = QRCodeDimension - 4;
            int FarAlign = QRCodeDimension - 7;

            double[,] Matrix = new double[8, 9];

            Matrix[0, 0] = 3.0;
            Matrix[0, 1] = 3.0;
            Matrix[0, 2] = 1.0;
            Matrix[0, 6] = -3.0 * Corner.TopLeftFinder.Col;
            Matrix[0, 7] = -3.0 * Corner.TopLeftFinder.Col;
            Matrix[0, 8] = Corner.TopLeftFinder.Col;

            Matrix[1, 0] = FarFinder;
            Matrix[1, 1] = 3.0;
            Matrix[1, 2] = 1.0;
            Matrix[1, 6] = -FarFinder * Corner.TopRightFinder.Col;
            Matrix[1, 7] = -3.0 * Corner.TopRightFinder.Col;
            Matrix[1, 8] = Corner.TopRightFinder.Col;

            Matrix[2, 0] = 3.0;
            Matrix[2, 1] = FarFinder;
            Matrix[2, 2] = 1.0;
            Matrix[2, 6] = -3.0 * Corner.BottomLeftFinder.Col;
            Matrix[2, 7] = -FarFinder * Corner.BottomLeftFinder.Col;
            Matrix[2, 8] = Corner.BottomLeftFinder.Col;

            Matrix[3, 0] = FarAlign;
            Matrix[3, 1] = FarAlign;
            Matrix[3, 2] = 1.0;
            Matrix[3, 6] = -FarAlign * ImageAlignCol;
            Matrix[3, 7] = -FarAlign * ImageAlignCol;
            Matrix[3, 8] = ImageAlignCol;

            Matrix[4, 3] = 3.0;
            Matrix[4, 4] = 3.0;
            Matrix[4, 5] = 1.0;
            Matrix[4, 6] = -3.0 * Corner.TopLeftFinder.Row;
            Matrix[4, 7] = -3.0 * Corner.TopLeftFinder.Row;
            Matrix[4, 8] = Corner.TopLeftFinder.Row;

            Matrix[5, 3] = FarFinder;
            Matrix[5, 4] = 3.0;
            Matrix[5, 5] = 1.0;
            Matrix[5, 6] = -FarFinder * Corner.TopRightFinder.Row;
            Matrix[5, 7] = -3.0 * Corner.TopRightFinder.Row;
            Matrix[5, 8] = Corner.TopRightFinder.Row;

            Matrix[6, 3] = 3.0;
            Matrix[6, 4] = FarFinder;
            Matrix[6, 5] = 1.0;
            Matrix[6, 6] = -3.0 * Corner.BottomLeftFinder.Row;
            Matrix[6, 7] = -FarFinder * Corner.BottomLeftFinder.Row;
            Matrix[6, 8] = Corner.BottomLeftFinder.Row;

            Matrix[7, 3] = FarAlign;
            Matrix[7, 4] = FarAlign;
            Matrix[7, 5] = 1.0;
            Matrix[7, 6] = -FarAlign * ImageAlignRow;
            Matrix[7, 7] = -FarAlign * ImageAlignRow;
            Matrix[7, 8] = ImageAlignRow;

            for (int Row = 0; Row < 8; Row++)
            {
                // If the element is zero, make it non zero by adding another row
                if (Matrix[Row, Row] == 0)
                {
                    int Row1;
                    for (Row1 = Row + 1; Row1 < 8 && Matrix[Row1, Row] == 0; Row1++) ;
                    if (Row1 == 8) throw new ApplicationException("Solve linear equations failed");

                    for (int Col = Row; Col < 9; Col++) Matrix[Row, Col] += Matrix[Row1, Col];
                }

                // make the diagonal element 1.0
                for (int Col = 8; Col > Row; Col--) Matrix[Row, Col] /= Matrix[Row, Row];

                // subtract current row from next rows to eliminate one value
                for (int Row1 = Row + 1; Row1 < 8; Row1++)
                {
                    for (int Col = 8; Col > Row; Col--) Matrix[Row1, Col] -= Matrix[Row, Col] * Matrix[Row1, Row];
                }
            }

            // go up from last row and eliminate all solved values
            for (int Col = 7; Col > 0; Col--) for (int Row = Col - 1; Row >= 0; Row--)
                {
                    Matrix[Row, 8] -= Matrix[Row, Col] * Matrix[Col, 8];
                }

            Trans4a = Matrix[0, 8];
            Trans4b = Matrix[1, 8];
            Trans4c = Matrix[2, 8];
            Trans4d = Matrix[3, 8];
            Trans4e = Matrix[4, 8];
            Trans4f = Matrix[5, 8];
            Trans4g = Matrix[6, 8];
            Trans4h = Matrix[7, 8];

            // set trans 4 mode
            Trans4Mode = true;
            return;
        }

        ////////////////////////////////////////////////////////////////////
        // Get version code bits top right
        ////////////////////////////////////////////////////////////////////

        internal int GetVersionOne()
        {
            int VersionCode = 0;
            for (int Index = 0; Index < 18; Index++)
            {
                if (GetModule(Index / 3, QRCodeDimension - 11 + (Index % 3))) VersionCode |= 1 << Index;
            }
            return TestVersionCode(VersionCode);
        }

        ////////////////////////////////////////////////////////////////////
        // Get version code bits bottom left
        ////////////////////////////////////////////////////////////////////

        internal int GetVersionTwo()
        {
            int VersionCode = 0;
            for (int Index = 0; Index < 18; Index++)
            {
                if (GetModule(QRCodeDimension - 11 + (Index % 3), Index / 3)) VersionCode |= 1 << Index;
            }
            return TestVersionCode(VersionCode);
        }

        ////////////////////////////////////////////////////////////////////
        // Test version code bits
        ////////////////////////////////////////////////////////////////////

        internal int TestVersionCode
                (
                int VersionCode
                )
        {
            // format info
            int Code = VersionCode >> 12;

            // test for exact match
            if (Code >= 7 && Code <= 40 && QRCode.VersionCodeArray[Code - 7] == VersionCode)
            {
#if DEBUG
                QRCodeTrace.Format("Version code exact match: {0:X4}, Version: {1}", VersionCode, Code);
#endif
                return Code;
            }

            // look for a match
            int BestInfo = 0;
            int Error = int.MaxValue;
            for (int Index = 0; Index < 34; Index++)
            {
                // test for exact match
                int ErrorBits = VersionCodeArray[Index] ^ VersionCode;
                if (ErrorBits == 0) return VersionCode >> 12;

                // count errors
                int ErrorCount = CountBits(ErrorBits);

                // save best result
                if (ErrorCount < Error)
                {
                    Error = ErrorCount;
                    BestInfo = Index;
                }
            }

#if DEBUG
            if (Error <= 3)
                QRCodeTrace.Format("Version code match with errors: {0:X4}, Version: {1}, Errors: {2}",
                    VersionCode, BestInfo + 7, Error);
            else
                QRCodeTrace.Format("Version code no match: {0:X4}", VersionCode);
#endif

            return Error <= 3 ? BestInfo + 7 : 0;
        }

        ////////////////////////////////////////////////////////////////////
        // Get format info around top left corner
        ////////////////////////////////////////////////////////////////////

        public int GetFormatInfoOne()
        {
            int Info = 0;
            for (int Index = 0; Index < 15; Index++)
            {
                if (GetModule(FormatInfoOne[Index, 0], FormatInfoOne[Index, 1])) Info |= 1 << Index;
            }
            return TestFormatInfo(Info);
        }

        ////////////////////////////////////////////////////////////////////
        // Get format info around top right and bottom left corners
        ////////////////////////////////////////////////////////////////////

        internal int GetFormatInfoTwo()
        {
            int Info = 0;
            for (int Index = 0; Index < 15; Index++)
            {
                int Row = FormatInfoTwo[Index, 0];
                if (Row < 0) Row += QRCodeDimension;
                int Col = FormatInfoTwo[Index, 1];
                if (Col < 0) Col += QRCodeDimension;
                if (GetModule(Row, Col)) Info |= 1 << Index;
            }
            return TestFormatInfo(Info);
        }

        ////////////////////////////////////////////////////////////////////
        // Test format info bits
        ////////////////////////////////////////////////////////////////////

        internal int TestFormatInfo
                (
                int FormatInfo
                )
        {
            // format info
            int Info = (FormatInfo ^ 0x5412) >> 10;

            // test for exact match
            if (QRCode.FormatInfoArray[Info] == FormatInfo)
            {
#if DEBUG
                QRCodeTrace.Format("Format info exact match: {0:X4}, EC: {1}, mask: {2}",
                    FormatInfo, FormatInfoToErrCode(Info >> 3).ToString(), Info & 7);
#endif
                return Info;
            }

            // look for a match
            int BestInfo = 0;
            int Error = int.MaxValue;
            for (int Index = 0; Index < 32; Index++)
            {
                int ErrorCount = CountBits(QRCode.FormatInfoArray[Index] ^ FormatInfo);
                if (ErrorCount < Error)
                {
                    Error = ErrorCount;
                    BestInfo = Index;
                }
            }

#if DEBUG
            if (Error <= 3)
                QRCodeTrace.Format("Format info match with errors: {0:X4}, EC: {1}, mask: {2}, errors: {3}",
                    FormatInfo, FormatInfoToErrCode(Info >> 3).ToString(), Info & 7, Error);
            else
                QRCodeTrace.Format("Format info no match: {0:X4}", FormatInfo);

#endif
            return Error <= 3 ? BestInfo : -1;
        }

        ////////////////////////////////////////////////////////////////////
        // Count Bits
        ////////////////////////////////////////////////////////////////////

        internal int CountBits
                (
                int Value
                )
        {
            int Count = 0;
            for (int Mask = 0x4000; Mask != 0; Mask >>= 1) if ((Value & Mask) != 0) Count++;
            return Count;
        }

        ////////////////////////////////////////////////////////////////////
        // Convert image to qr code matrix and test fixed modules
        ////////////////////////////////////////////////////////////////////

        internal void ConvertImageToMatrix()
        {
            // loop for all modules
            int FixedCount = 0;
            int ErrorCount = 0;
            for (int Row = 0; Row < QRCodeDimension; Row++) for (int Col = 0; Col < QRCodeDimension; Col++)
                {
                    // the module (Row, Col) is not a fixed module 
                    if ((BaseMatrix[Row, Col] & Fixed) == 0)
                    {
                        if (GetModule(Row, Col)) BaseMatrix[Row, Col] |= Black;
                    }

                    // fixed module
                    else
                    {
                        // total fixed modules
                        FixedCount++;

                        // test for error
                        if ((GetModule(Row, Col) ? Black : White) != (BaseMatrix[Row, Col] & 1)) ErrorCount++;
                    }
                }

#if DEBUG
            if (ErrorCount == 0)
            {
                QRCodeTrace.Write("Fixed modules no error");
            }
            else if (ErrorCount <= FixedCount * ErrCorrPercent[(int)ErrorCorrection] / 100)
            {
                QRCodeTrace.Format("Fixed modules some errors: {0} / {1}", ErrorCount, FixedCount);
            }
            else
            {
                QRCodeTrace.Format("Fixed modules too many errors: {0} / {1}", ErrorCount, FixedCount);
            }
#endif
            if (ErrorCount > FixedCount * ErrCorrPercent[(int)ErrorCorrection] / 100)
                throw new ApplicationException("Fixed modules error");
            return;
        }

        ////////////////////////////////////////////////////////////////////
        // Unload matrix data from base matrix
        ////////////////////////////////////////////////////////////////////

        internal void UnloadDataFromMatrix()
        {
            // input array pointer initialization
            int Ptr = 0;
            int PtrEnd = 8 * MaxCodewords;
            CodewordsArray = new byte[MaxCodewords];

            // bottom right corner of output matrix
            int Row = QRCodeDimension - 1;
            int Col = QRCodeDimension - 1;

            // step state
            int State = 0;
            for (;;)
            {
                // current module is data
                if ((MaskMatrix[Row, Col] & NonData) == 0)
                {
                    // unload current module with
                    if ((MaskMatrix[Row, Col] & 1) != 0) CodewordsArray[Ptr >> 3] |= (byte)(1 << (7 - (Ptr & 7)));
                    if (++Ptr == PtrEnd) break;
                }

                // current module is non data and vertical timing line condition is on
                else if (Col == 6) Col--;

                // update matrix position to next module
                switch (State)
                {
                    // going up: step one to the left
                    case 0:
                        Col--;
                        State = 1;
                        continue;

                    // going up: step one row up and one column to the right
                    case 1:
                        Col++;
                        Row--;
                        // we are not at the top, go to state 0
                        if (Row >= 0)
                        {
                            State = 0;
                            continue;
                        }
                        // we are at the top, step two columns to the left and start going down
                        Col -= 2;
                        Row = 0;
                        State = 2;
                        continue;

                    // going down: step one to the left
                    case 2:
                        Col--;
                        State = 3;
                        continue;

                    // going down: step one row down and one column to the right
                    case 3:
                        Col++;
                        Row++;
                        // we are not at the bottom, go to state 2
                        if (Row < QRCodeDimension)
                        {
                            State = 2;
                            continue;
                        }
                        // we are at the bottom, step two columns to the left and start going up
                        Col -= 2;
                        Row = QRCodeDimension - 1;
                        State = 0;
                        continue;
                }
            }
            return;
        }

        ////////////////////////////////////////////////////////////////////
        // Restore interleave data and error correction blocks
        ////////////////////////////////////////////////////////////////////

        internal void RestoreBlocks()
        {
            // allocate temp codewords array
            byte[] TempArray = new byte[MaxCodewords];

            // total blocks
            int TotalBlocks = BlocksGroup1 + BlocksGroup2;

            // create array of data blocks starting point
            int[] Start = new int[TotalBlocks];
            for (int Index = 1; Index < TotalBlocks; Index++) Start[Index] = Start[Index - 1] + (Index <= BlocksGroup1 ? DataCodewordsGroup1 : DataCodewordsGroup2);

            // step one. iterleave base on group one length
            int PtrEnd = DataCodewordsGroup1 * TotalBlocks;

            // restore group one and two
            int Ptr;
            int Block = 0;
            for (Ptr = 0; Ptr < PtrEnd; Ptr++)
            {
                TempArray[Start[Block]] = CodewordsArray[Ptr];
                Start[Block]++;
                Block++;
                if (Block == TotalBlocks) Block = 0;
            }

            // restore group two
            if (DataCodewordsGroup2 > DataCodewordsGroup1)
            {
                // step one. iterleave base on group one length
                PtrEnd = MaxDataCodewords;

                Block = BlocksGroup1;
                for (; Ptr < PtrEnd; Ptr++)
                {
                    TempArray[Start[Block]] = CodewordsArray[Ptr];
                    Start[Block]++;
                    Block++;
                    if (Block == TotalBlocks) Block = BlocksGroup1;
                }
            }

            // create array of error correction blocks starting point
            Start[0] = MaxDataCodewords;
            for (int Index = 1; Index < TotalBlocks; Index++) Start[Index] = Start[Index - 1] + ErrCorrCodewords;

            // restore all groups
            PtrEnd = MaxCodewords;
            Block = 0;
            for (; Ptr < PtrEnd; Ptr++)
            {
                TempArray[Start[Block]] = CodewordsArray[Ptr];
                Start[Block]++;
                Block++;
                if (Block == TotalBlocks) Block = 0;
            }

            // save result
            CodewordsArray = TempArray;
            return;
        }

        ////////////////////////////////////////////////////////////////////
        // Calculate Error Correction
        ////////////////////////////////////////////////////////////////////

        protected void CalculateErrorCorrection()
        {
            // total error count
            int TotalErrorCount = 0;

            // set generator polynomial array
            byte[] Generator = GenArray[ErrCorrCodewords - 7];

            // error correcion calculation buffer
            int BufSize = Math.Max(DataCodewordsGroup1, DataCodewordsGroup2) + ErrCorrCodewords;
            byte[] ErrCorrBuff = new byte[BufSize];

            // initial number of data codewords
            int DataCodewords = DataCodewordsGroup1;
            int BuffLen = DataCodewords + ErrCorrCodewords;

            // codewords pointer
            int DataCodewordsPtr = 0;

            // codewords buffer error correction pointer
            int CodewordsArrayErrCorrPtr = MaxDataCodewords;

            // loop one block at a time
            int TotalBlocks = BlocksGroup1 + BlocksGroup2;
            for (int BlockNumber = 0; BlockNumber < TotalBlocks; BlockNumber++)
            {
                // switch to group2 data codewords
                if (BlockNumber == BlocksGroup1)
                {
                    DataCodewords = DataCodewordsGroup2;
                    BuffLen = DataCodewords + ErrCorrCodewords;
                }

                // copy next block of codewords to the buffer and clear the remaining part
                Array.Copy(CodewordsArray, DataCodewordsPtr, ErrCorrBuff, 0, DataCodewords);
                Array.Copy(CodewordsArray, CodewordsArrayErrCorrPtr, ErrCorrBuff, DataCodewords, ErrCorrCodewords);

                // make a duplicate
                byte[] CorrectionBuffer = (byte[])ErrCorrBuff.Clone();

                // error correction polynomial division
                ReedSolomon.PolynominalDivision(ErrCorrBuff, BuffLen, Generator, ErrCorrCodewords);

                // test for error
                int Index;
                for (Index = 0; Index < ErrCorrCodewords && ErrCorrBuff[DataCodewords + Index] == 0; Index++) ;
                if (Index < ErrCorrCodewords)
                {
                    // correct the error
                    int ErrorCount = ReedSolomon.CorrectData(CorrectionBuffer, BuffLen, ErrCorrCodewords);
                    if (ErrorCount <= 0)
                    {
                        throw new ApplicationException("Data is damaged. Error correction failed");
                    }

                    TotalErrorCount += ErrorCount;

                    // fix the data
                    Array.Copy(CorrectionBuffer, 0, CodewordsArray, DataCodewordsPtr, DataCodewords);
                }

                // update codewords array to next buffer
                DataCodewordsPtr += DataCodewords;

                // update pointer				
                CodewordsArrayErrCorrPtr += ErrCorrCodewords;
            }

#if DEBUG
            if (TotalErrorCount == 0) QRCodeTrace.Write("No data errors");
            else QRCodeTrace.Write("Error correction applied to data. Total errors: " + TotalErrorCount.ToString());
#endif
            return;
        }

        ////////////////////////////////////////////////////////////////////
        // Convert bit array to byte array
        ////////////////////////////////////////////////////////////////////

        internal byte[] DecodeData()
        {
            // bit buffer initial condition
            BitBuffer = (UInt32)((CodewordsArray[0] << 24) | (CodewordsArray[1] << 16) | (CodewordsArray[2] << 8) | CodewordsArray[3]);
            BitBufferLen = 32;
            CodewordsPtr = 4;

            // allocate data byte list
            List<byte> DataSeg = new List<byte>();

            // data might be made of blocks
            for (;;)
            {
                // first 4 bits is mode indicator
                EncodingMode EncodingMode = (EncodingMode)ReadBitsFromCodewordsArray(4);

                // end of data
                if (EncodingMode <= 0) break;

                // read data length
                int DataLength = ReadBitsFromCodewordsArray(DataLengthBits(EncodingMode));
                if (DataLength < 0)
                {
                    throw new ApplicationException("Premature end of data (DataLengh)");
                }

                // save start of segment
                int SegStart = DataSeg.Count;

                // switch based on encode mode
                // numeric code indicator is 0001, alpha numeric 0010, byte 0100
                switch (EncodingMode)
                {
                    // numeric mode
                    case EncodingMode.Numeric:
                        // encode digits in groups of 2
                        int NumericEnd = (DataLength / 3) * 3;
                        for (int Index = 0; Index < NumericEnd; Index += 3)
                        {
                            int Temp = ReadBitsFromCodewordsArray(10);
                            if (Temp < 0)
                            {
                                throw new ApplicationException("Premature end of data (Numeric 1)");
                            }
                            DataSeg.Add(DecodingTable[Temp / 100]);
                            DataSeg.Add(DecodingTable[(Temp % 100) / 10]);
                            DataSeg.Add(DecodingTable[Temp % 10]);
                        }

                        // we have one character remaining
                        if (DataLength - NumericEnd == 1)
                        {
                            int Temp = ReadBitsFromCodewordsArray(4);
                            if (Temp < 0)
                            {
                                throw new ApplicationException("Premature end of data (Numeric 2)");
                            }
                            DataSeg.Add(DecodingTable[Temp]);
                        }

                        // we have two character remaining
                        else if (DataLength - NumericEnd == 2)
                        {
                            int Temp = ReadBitsFromCodewordsArray(7);
                            if (Temp < 0)
                            {
                                throw new ApplicationException("Premature end of data (Numeric 3)");
                            }
                            DataSeg.Add(DecodingTable[Temp / 10]);
                            DataSeg.Add(DecodingTable[Temp % 10]);
                        }
                        break;

                    // alphanumeric mode
                    case EncodingMode.AlphaNumeric:
                        // encode digits in groups of 2
                        int AlphaNumEnd = (DataLength / 2) * 2;
                        for (int Index = 0; Index < AlphaNumEnd; Index += 2)
                        {
                            int Temp = ReadBitsFromCodewordsArray(11);
                            if (Temp < 0)
                            {
                                throw new ApplicationException("Premature end of data (Alpha Numeric 1)");
                            }
                            DataSeg.Add(DecodingTable[Temp / 45]);
                            DataSeg.Add(DecodingTable[Temp % 45]);
                        }

                        // we have one character remaining
                        if (DataLength - AlphaNumEnd == 1)
                        {
                            int Temp = ReadBitsFromCodewordsArray(6);
                            if (Temp < 0)
                            {
                                throw new ApplicationException("Premature end of data (Alpha Numeric 2)");
                            }
                            DataSeg.Add(DecodingTable[Temp]);
                        }
                        break;

                    // byte mode					
                    case EncodingMode.Byte:
                        // append the data after mode and character count
                        for (int Index = 0; Index < DataLength; Index++)
                        {
                            int Temp = ReadBitsFromCodewordsArray(8);
                            if (Temp < 0)
                            {
                                throw new ApplicationException("Premature end of data (byte mode)");
                            }
                            DataSeg.Add((byte)Temp);
                        }
                        break;

                    default:
                        throw new ApplicationException(string.Format("Encoding mode not supported {0}", EncodingMode.ToString()));
                }

                if (DataLength != DataSeg.Count - SegStart) throw new ApplicationException("Data encoding length in error");
            }

            // save data
            return DataSeg.ToArray();
        }

        ////////////////////////////////////////////////////////////////////
        // Read data from codeword array
        ////////////////////////////////////////////////////////////////////

        internal int ReadBitsFromCodewordsArray
                (
                int Bits
                )
        {
            if (Bits > BitBufferLen) return -1;
            int Data = (int)(BitBuffer >> (32 - Bits));
            BitBuffer <<= Bits;
            BitBufferLen -= Bits;
            while (BitBufferLen <= 24 && CodewordsPtr < MaxDataCodewords)
            {
                BitBuffer |= (UInt32)(CodewordsArray[CodewordsPtr++] << (24 - BitBufferLen));
                BitBufferLen += 8;
            }
            return Data;
        }
    }
}
