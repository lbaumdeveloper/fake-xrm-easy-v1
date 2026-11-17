namespace FakeXrmEasy
{
    /// <summary>
    /// Describes an entity image registered on a plugin step for pipeline simulation.
    /// </summary>
    public class PluginStepImage
    {
        /// <summary>
        /// Creates an image definition.
        /// </summary>
        /// <param name="name">The key under which the image is available in PreEntityImages or PostEntityImages.</param>
        /// <param name="imageType">Whether the image is a pre image, a post image or both.</param>
        /// <param name="attributes">The attributes included in the image. All attributes are included when none are given.</param>
        public PluginStepImage(string name, ProcessingStepImageType imageType, params string[] attributes)
        {
            Name = name;
            ImageType = imageType;
            Attributes = attributes ?? new string[0];
        }

        public string Name { get; }

        public ProcessingStepImageType ImageType { get; }

        public string[] Attributes { get; }
    }
}
