namespace GroundStation.DigitalTwin
{
    public interface IDigitalTwinIngress
    {
        DigitalTwinApplyStatus LastApplyStatus { get; }
        bool TryApplyDigitalTwinJson(string json);
        string BuildLastAckJson();
    }
}
