using Esi.AI.Models;
using OpenVinoSharp;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Esi.AI.Core.ModelLoading;

/// <summary>Creates OpenVINO image tensors from transport-neutral chat images.</summary>
public static class OpenVinoImageTensorFactory
{
    /// <summary>Decodes all images in message order as RGB NHWC tensors for VLMPipeline.</summary>
    /// <param name="messages">The parsed chat messages containing local image bytes.</param>
    /// <returns>The tensors owned by the caller.</returns>
    public static Tensor[] Create(IReadOnlyList<ChatMessage> messages)
    {
        var images = messages
            .SelectMany(message => message.Images ?? [])
            .ToArray();
        var tensors = new List<Tensor>(images.Length);

        try
        {
            foreach (var image in images)
                tensors.Add(Create(image));

            return tensors.ToArray();
        }
        catch
        {
            foreach (var tensor in tensors)
                tensor.Dispose();
            throw;
        }
    }

    private static Tensor Create(ChatImage image)
    {
        using var decodedImage = Image.Load<Rgb24>(image.Data);
        var pixelData = new byte[checked(decodedImage.Width * decodedImage.Height * 3)];
        decodedImage.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < decodedImage.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < decodedImage.Width; x++)
                {
                    var source = row[x];
                    var target = (y * decodedImage.Width + x) * 3;
                    pixelData[target] = source.R;
                    pixelData[target + 1] = source.G;
                    pixelData[target + 2] = source.B;
                }
            }
        });

        using var shape = new Shape([1, decodedImage.Height, decodedImage.Width, 3]);
        var tensor = new Tensor(shape, ElementType.U8);
        tensor.SetData(pixelData);
        return tensor;
    }
}