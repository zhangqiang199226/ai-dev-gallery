// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using AIDevGallery.Models;
using AIDevGallery.Samples.Attributes;
using AIDevGallery.Samples.SharedCode;
using AIDevGallery.Utils;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using Windows.Storage.Pickers;

namespace AIDevGallery.Samples.OpenSourceModels;

[GallerySample(
    Model1Types = [ModelType.MobileNet, ModelType.ResNet, ModelType.SqueezeNet],
    Scenario = ScenarioType.ImageClassifyImage,
    NugetPackageReferences = [
        "System.Drawing.Common",
        "Microsoft.ML.OnnxRuntime.DirectML",
        "Microsoft.ML.OnnxRuntime.Extensions"
    ],
    SharedCode = [
        SharedCodeEnum.Prediction,
        SharedCodeEnum.ImageNetLabels,
        SharedCodeEnum.ImageNet,
        SharedCodeEnum.BitmapFunctions,
        SharedCodeEnum.DeviceUtils
    ],
    AssetFilenames = [
        "team.jpg"
    ],
    Name = "ImageNet Image Classification",
    Id = "09d73ba7-b877-45f9-9de6-41898ab4d339",
    Icon = "\uE8B9")]
internal sealed partial class ImageClassification : BaseSamplePage
{
    private InferenceSession? _inferenceSession;

    public ImageClassification()
    {
        this.Unloaded += (s, e) => _inferenceSession?.Dispose();
        this.Loaded += (s, e) => Page_Loaded(); // <exclude-line>
        this.InitializeComponent();
    }

    protected override async Task LoadModelAsync(SampleNavigationParameters sampleParams)
    {
        var hardwareAccelerator = sampleParams.HardwareAccelerator;
        await InitModel(sampleParams.ModelPath, hardwareAccelerator);
        sampleParams.NotifyCompletion();

        await ClassifyImage(Path.Join(Windows.ApplicationModel.Package.Current.InstalledLocation.Path, "Assets", "team.jpg"));
    }

    // <exclude>
    private void Page_Loaded()
    {
        UploadImageButton.Focus(FocusState.Programmatic);
    }

    // </exclude>
    private Task InitModel(string modelPath, HardwareAccelerator hardwareAccelerator)
    {
        return Task.Run(() =>
        {
            if (_inferenceSession != null)
            {
                return;
            }

            SessionOptions sessionOptions = new();
            sessionOptions.RegisterOrtExtensions();
            if (hardwareAccelerator == HardwareAccelerator.DML)
            {
                sessionOptions.AppendExecutionProvider_DML(DeviceUtils.GetBestDeviceId());
            }
            else if (hardwareAccelerator == HardwareAccelerator.QNN)
            {
                Dictionary<string, string> options = new()
                    {
                        { "backend_path", "QnnHtp.dll" },
                        { "htp_performance_mode", "high_performance" },
                        { "htp_graph_finalization_optimization_mode", "3" }
                    };
                sessionOptions.AppendExecutionProvider("QNN", options);
            }

            _inferenceSession = new InferenceSession(modelPath, sessionOptions);
        });
    }

    private async void UploadImageButton_Click(object sender, RoutedEventArgs e)
    {
        var window = new Window();
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);

        var picker = new FileOpenPicker();

        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

        picker.FileTypeFilter.Add(".png");
        picker.FileTypeFilter.Add(".jpeg");
        picker.FileTypeFilter.Add(".jpg");

        picker.ViewMode = PickerViewMode.Thumbnail;

        var file = await picker.PickSingleFileAsync();
        if (file != null)
        {
            UploadImageButton.Focus(FocusState.Programmatic);
            SendSampleInteractedEvent("FileSelected"); // <exclude-line>
            await ClassifyImage(file.Path);
        }
    }

    private async Task ClassifyImage(string filePath)
    {
        if (!Path.Exists(filePath) || _inferenceSession == null)
        {
            return;
        }

        // Grab model metadata
        var inputName = _inferenceSession.InputNames[0];
        var inputMetadata = _inferenceSession.InputMetadata[inputName];
        var dimensions = inputMetadata.Dimensions;

        // Set batch size to 1
        int batchSize = 1;
        dimensions[0] = batchSize;

        int inputWidth = dimensions[2];
        int inputHeight = dimensions[3];

        BitmapImage bitmapImage = new(new Uri(filePath));
        UploadedImage.Source = bitmapImage;
        NarratorHelper.AnnounceImageChanged(UploadedImage, "Image changed: new upload."); // <exclude-line>

        var predictions = await Task.Run(() =>
        {
            Bitmap image = new(filePath);

            // Resize image
            var resizedImage = BitmapFunctions.ResizeBitmap(image, inputWidth, inputHeight);
            image.Dispose();
            image = resizedImage;

            // Preprocess image
            Tensor<float> input = new DenseTensor<float>(dimensions);
            input = BitmapFunctions.PreprocessBitmapWithStdDev(image, input);
            image.Dispose();

            // Setup inputs
            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor(inputName, input)
            };

            // Run inference
            using IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results = _inferenceSession!.Run(inputs);

            // Postprocess to get softmax vector
            IEnumerable<float> output = results[0].AsEnumerable<float>();
            return ImageNet.GetSoftmax(output);
        });

        // Populates table of results
        ImageNet.DisplayPredictions(predictions, PredictionsStackPanel);
    }
}