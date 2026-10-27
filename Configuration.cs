using Dalamud.Configuration;
using StoryLens.Core;

namespace StoryLens;

internal sealed class Configuration : ConfigurationData, IPluginConfiguration
{
    public void Validate(QuestCatalog catalog) => base.Validate(catalog.WorldIds);
}
