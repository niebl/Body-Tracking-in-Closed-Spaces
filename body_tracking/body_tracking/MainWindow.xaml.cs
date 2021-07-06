﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

using System.ComponentModel;

// Kinect V2 Extensions
using Microsoft.Kinect;

namespace body_tracking
{
    // class, containing available frame types
    public enum DisplayFrameType
    {
        Infrared,
        Color,
        Depth
    }
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        //setup interface option selection
        private const DisplayFrameType DEFAULT_DISPLAYFRAMETYPE = DisplayFrameType.Depth;

        //Kinect Sensor
        private KinectSensor kinectSensor = null;

        //image object, this time it's more flexible
        private WriteableBitmap bitmap = null;
        private FrameDescription currentFrameDescription;
        private DisplayFrameType currentDisplayFrameType;
        //reader for the camera sources
        private MultiSourceFrameReader multiSourceFrameReader = null;

        /** Scale the values from 0 to 255 **/

        /// Setup the limits (post processing)of the infrared data that we will render.
        /// Increasing or decreasing this value sets a brightness "wall" either closer or further away.
        private const float InfraredOutputValueMinimum = 0.01f;
        private const float InfraredOutputValueMaximum = 1.0f;
        private const float InfraredSourceValueMaximum = ushort.MaxValue;
        /// Scale of Infrared source data
        private const float InfraredSourceScale = 0.75f;

        /* Since depth info is used, it needs to be mapped*/
        private const int MapDepthToByte = 8000 / 256;
        //Intermediate storage for frame data converted to color
        private byte[] depthPixels = null;


        public MainWindow()
        {
            /*
             Two steps to set up display Frame types
            1. get FrameDescription
            2. create the bitmap to display
             */

            // init sensor
            this.kinectSensor = KinectSensor.GetDefault();

            // open the reader for the  frames, this time it is a global object with 3 options: Infrared, Color and Depth
            this.multiSourceFrameReader = this.kinectSensor.OpenMultiSourceFrameReader(FrameSourceTypes.Infrared | FrameSourceTypes.Color | FrameSourceTypes.Depth);
            //HAndler for frame arrival according to the frame source - defined method
            this.multiSourceFrameReader.MultiSourceFrameArrived += this.Reader_MultiSourceFrameArrived;

            SetupCurrentDisplay(DEFAULT_DISPLAYFRAMETYPE);

            // use the window object as the view model in this simple example
            this.DataContext = this;

            // open the sensor
            this.kinectSensor.Open();
            InitializeComponent();
        }

        public ImageSource ImageSource
        {
            get
            {
                return this.bitmap;
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public FrameDescription CurrentFrameDescription
        {
            get { return this.currentFrameDescription; }
            set
            {
                if(this.currentFrameDescription != value)
                {
                    this.currentFrameDescription = value;
                    if (this.PropertyChanged != null)
                    {
                        this.PropertyChanged(this, new PropertyChangedEventArgs("CurrentFrameDescription"));
                    }
                }
            }
        }

        //set up the display in a way that it knows how to relay the imagery no matter if its IR, color, or depth
        private void SetupCurrentDisplay(DisplayFrameType newDisplayFrameType)
        {
            currentDisplayFrameType = newDisplayFrameType;
            Console.WriteLine(currentDisplayFrameType);

            switch (currentDisplayFrameType)
            {
                //TODO: refactor such recurring lines into their own functions for practice
                case DisplayFrameType.Infrared:
                    FrameDescription infraredFrameDescription = this.kinectSensor.InfraredFrameSource.FrameDescription;
                    this.CurrentFrameDescription = infraredFrameDescription;
                    this.bitmap = new WriteableBitmap(infraredFrameDescription.Width, infraredFrameDescription.Height, 96.0, 96.0, PixelFormats.Gray32Float, null);
                    break;

                case DisplayFrameType.Color:
                    FrameDescription colorFrameDescription = this.kinectSensor.ColorFrameSource.FrameDescription;
                    this.CurrentFrameDescription = colorFrameDescription;
                    // create the bitmap to display
                    this.bitmap = new WriteableBitmap(colorFrameDescription.Width, colorFrameDescription.Height, 96.0, 96.0, PixelFormats.Bgra32, null);
                    break;

                case DisplayFrameType.Depth:
                    FrameDescription depthFrameDescription = this.kinectSensor.DepthFrameSource.FrameDescription;
                    this.CurrentFrameDescription = depthFrameDescription;
                    // allocate space to put the pixels being received and converted
                    this.depthPixels = new byte[depthFrameDescription.Width * depthFrameDescription.Height];
                    // create the bitmap to display
                    this.bitmap = new WriteableBitmap(depthFrameDescription.Width, depthFrameDescription.Height, 96.0, 96.0, PixelFormats.Gray8, null);
                    break;

                default:
                    break;
            }
        }

        private void ShowColorFrame(ColorFrame colorFrame)
        {
            if (colorFrame != null)
            {
                FrameDescription colorFrameDescription = colorFrame.FrameDescription;

                //LockRawImageBuffer needs to be understood
                using (KinectBuffer colorBuffer = colorFrame.LockRawImageBuffer())
                {
                    this.bitmap.Lock();

                    if((colorFrameDescription.Width == this.bitmap.PixelWidth) &&
                       (colorFrameDescription.Height == this.bitmap.PixelHeight))
                    {
                        colorFrame.CopyConvertedFrameDataToIntPtr(
                            this.bitmap.BackBuffer, 
                            (uint)(colorFrameDescription.Width * colorFrameDescription.Height * 4), 
                            ColorImageFormat.Bgra
                        );

                        this.bitmap.AddDirtyRect(new Int32Rect(0, 0, this.bitmap.PixelWidth, this.bitmap.PixelHeight));
                    }
                    this.bitmap.Unlock();
                    FrameDisplayImage.Source = this.bitmap;
                }
            }
        }

        private void ShowInfraredFrame(InfraredFrame infraredFrame)
        {

            //what does "IDisposable" mean?
            // InfraredFrame is IDisposable

            if (infraredFrame != null)
            {
                FrameDescription infraredFrameDescription = infraredFrame.FrameDescription;
                /// We are using WPF (Windows Presentation Foundation)
                using (KinectBuffer infraredBuffer = infraredFrame.LockImageBuffer())
                {
                    // verify data and write the new infrared frame data to the display bitmap
                    if (((infraredFrameDescription.Width * infraredFrameDescription.Height) == (infraredBuffer.Size / infraredFrameDescription.BytesPerPixel)) &&
                        (infraredFrameDescription.Width == this.bitmap.PixelWidth) && (infraredFrameDescription.Height == this.bitmap.PixelHeight))
                    {
                        this.ProcessInfraredFrameData(infraredBuffer.UnderlyingBuffer, infraredBuffer.Size, infraredFrameDescription);
                    }
                }
            }
        }

        private void ShowDepthFrame(DepthFrame depthFrame)
        {
            if (depthFrame != null)
            {
                FrameDescription depthFrameDescription = depthFrame.FrameDescription;

                using (KinectBuffer depthBuffer = depthFrame.LockImageBuffer())
                {
                    // verify data and write the color data to the display bitmap
                    if (((depthFrameDescription.Width * depthFrameDescription.Height) == (depthBuffer.Size / depthFrameDescription.BytesPerPixel)) &&
                        (depthFrameDescription.Width == this.bitmap.PixelWidth) && (depthFrameDescription.Height == this.bitmap.PixelHeight))
                    {
                        // Note: In order to see the full range of depth (including the less reliable far field depth) we are setting maxDepth to the extreme potential depth threshold
                        // TODO: buttons to change the depth could be fun
                        ushort maxDepth = ushort.MaxValue;
                        this.ProcessDepthFrameData(depthBuffer.UnderlyingBuffer, depthBuffer.Size, depthFrame.DepthMinReliableDistance, maxDepth, depthFrameDescription);

                    }
                }
            }
        }

        private void Button_Color(object sender, RoutedEventArgs e)
        {
            SetupCurrentDisplay(DisplayFrameType.Color);

        }

        private void Button_Infrared(object sender, RoutedEventArgs e)
        {
            SetupCurrentDisplay(DisplayFrameType.Infrared);
        }

        private void Button_Depth(object sender, RoutedEventArgs e)
        {
            SetupCurrentDisplay(DisplayFrameType.Depth);
        }

        /// <summary>
        /// Handles the frame data arriving from the sensor.
        /// now has the ability to handle multiple frame types
        /// </summary>
        /// <param name="sender">object sending the event</param>
        /// <param name="e">event arguments</param>
        /// 
        private void Reader_MultiSourceFrameArrived(object sender, MultiSourceFrameArrivedEventArgs e)
        {
            MultiSourceFrame multiSourceFrame = e.FrameReference.AcquireFrame();

            switch (currentDisplayFrameType)
            {
                case DisplayFrameType.Infrared:
                    using (InfraredFrame infraredFrame = multiSourceFrame.InfraredFrameReference.AcquireFrame())
                    {
                        ShowInfraredFrame(infraredFrame);
                    }
                    break;

                case DisplayFrameType.Color:
                    using (ColorFrame colorFrame = multiSourceFrame.ColorFrameReference.AcquireFrame())
                    {
                        ShowColorFrame(colorFrame);
                    }
                    break;

                case DisplayFrameType.Depth:
                    using (DepthFrame depthFrame = multiSourceFrame.DepthFrameReference.AcquireFrame())
                    {
                        ShowDepthFrame(depthFrame);
                    }
                    break;

                default:
                    break;
            }
        }

        //helping function for ShowDepthFrame
        private unsafe void ProcessDepthFrameData(IntPtr depthFrameData, uint depthFrameDataSize, ushort minDepth, ushort maxDepth, FrameDescription depthFrameDescription)
        {
            ushort* frameData = (ushort*)depthFrameData;
            this.bitmap.Lock();

            // depth to visual representation
            for (int i = 0; i < (int)(depthFrameDataSize / depthFrameDescription.BytesPerPixel); ++i)
            {
                ushort depth = frameData[i];

                //the depth value is mapped to the byte range
                // I heard a look up table might be more efficient in this case... subject for a possible TODO
                //the ? and : are conditionals
                this.depthPixels[i] = (byte)(depth >= minDepth && depth <= maxDepth ? (depth / MapDepthToByte) : 0);
            }
            this.bitmap.WritePixels(new Int32Rect(0, 0, this.bitmap.PixelWidth, this.bitmap.PixelHeight), this.depthPixels, this.bitmap.PixelWidth, 0);

            this.bitmap.Unlock();
            FrameDisplayImage.Source = this.bitmap;
        }

        /// Directly accesses the underlying image buffer of the InfraredFrame to create a displayable bitmap.
        /// This function requires the /unsafe compiler option as we make use of direct access to the native memory pointed to by the infraredFrameData pointer.
        /// Activate "unsafe" in the solution properties > on the left >Build > Check Allow unsafe code
        /// <param name="infraredFrameData">Pointer to the InfraredFrame image data</param>
        /// <param name="infraredFrameDataSize">Size of the InfraredFrame image data</param>
        private unsafe void ProcessInfraredFrameData(IntPtr infraredFrameData, uint infraredFrameDataSize, FrameDescription infraredFrameDescription)
        {
            // infrared frame data is a 16 bit value
            ushort* frameData = (ushort*)infraredFrameData;

            // lock the target bitmap
            this.bitmap.Lock();

            // get the pointer to the bitmap's back buffer
            float* backBuffer = (float*)this.bitmap.BackBuffer;

            // process the infrared data
            for (int i = 0; i < (int)(infraredFrameDataSize / infraredFrameDescription.BytesPerPixel); ++i)
            {
                // since we are displaying the image as a normalized grey scale image, we need to convert from
                // the ushort data (as provided by the InfraredFrame) to a value from [InfraredOutputValueMinimum, InfraredOutputValueMaximum]
                backBuffer[i] = Math.Min(InfraredOutputValueMaximum, (((float)frameData[i] / InfraredSourceValueMaximum * InfraredSourceScale) * (1.0f - InfraredOutputValueMinimum)) + InfraredOutputValueMinimum);
            }

            // mark the entire bitmap as needing to be drawn
            this.bitmap.AddDirtyRect(new Int32Rect(0, 0, this.bitmap.PixelWidth, this.bitmap.PixelHeight));

            // unlock the bitmap
            this.bitmap.Unlock();
            FrameDisplayImage.Source = this.bitmap;
        }

    }
}
