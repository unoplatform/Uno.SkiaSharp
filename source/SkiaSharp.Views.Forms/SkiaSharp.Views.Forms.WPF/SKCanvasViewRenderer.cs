﻿using Xamarin.Forms.Platform.WPF;

using SKFormsView = SkiaSharp.Views.Forms.SKCanvasView;
using SKNativeView = SkiaSharp.Views.WPF.SKElement;

[assembly: ExportRenderer(typeof(SKFormsView), typeof(SkiaSharp.Views.Forms.SKCanvasViewRenderer))]

namespace SkiaSharp.Views.Forms
{
	public class SKCanvasViewRenderer : SKCanvasViewRendererBase<SKFormsView, SKNativeView>
	{
	}
}
