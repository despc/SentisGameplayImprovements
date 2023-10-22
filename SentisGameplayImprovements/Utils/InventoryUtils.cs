using VRage.Game;

namespace SentisGameplayImprovements
{
    public static class InventoryUtils
    {
        public static MyDefinitionId GetItemDefenition(string type, string id)
        {
            switch (type)
            {
                case "Component":
                {
                    return new MyDefinitionId(typeof(MyObjectBuilder_Component), id);
                }
                case "Ingot":
                {
                    return new MyDefinitionId(typeof(MyObjectBuilder_Ingot), id);
                }
                case "Ore":
                {
                    return new MyDefinitionId(typeof(MyObjectBuilder_Ore), id);
                }
            }

            return new MyDefinitionId(typeof(MyObjectBuilder_PhysicalObject), id);
        }
    }
}