namespace GroundStation.DigitalTwin
{
    public interface IDigitalTwinAckSink
    {
        void PublishAck(string ackJson);
    }
}
