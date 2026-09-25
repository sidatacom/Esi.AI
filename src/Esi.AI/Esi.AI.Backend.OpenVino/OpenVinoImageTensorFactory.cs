using Esi.AI.Models;
using OpenVinoSharp;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Esi.AI.Backend.OpenVino;

internal static class OpenVinoImageTensorFactory
{
    public static Tensor[] Create(IReadOnlyList<ChatMessage> messages)
    {
        var images = messages.SelectMany(message => message.Images ?? []).ToArray();
        var tensors = new List<Tensor>(images.Length);
        try
        {
            foreach (var image in images)
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
                tensors.Add(tensor);
            }

            return tensors.ToArray();
        }
        catch
        {
            foreach (var tensor in tensors)
                tensor.Dispose();
            throw;
        }
    }
}