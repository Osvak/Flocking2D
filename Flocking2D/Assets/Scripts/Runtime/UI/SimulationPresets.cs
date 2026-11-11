public enum SimulationPreset
{
    Custom,
    Murmuration,
    TightSchool,
    Chaotic
}

public static class SimulationPresets
{
    public static SimulationConfigData Apply(SimulationPreset preset, SimulationConfigData current, SimulationConfigData custom)
    {
        switch (preset)
        {
            case SimulationPreset.Murmuration:
                return WithBehavior(current, Murmuration());
            case SimulationPreset.TightSchool:
                return WithBehavior(current, TightSchool());
            case SimulationPreset.Chaotic:
                return WithBehavior(current, Chaotic());
            default:
                return WithBehavior(current, custom);
        }
    }

    private static SimulationConfigData Murmuration()
    {
        return new SimulationConfigData
        {
            alignmentWeight = 1.5f,
            separationWeight = 2f,
            cohesionWeight = 1f,
            edgeWeight = 3f,
            turnRate = 6f,
            fieldOfViewDegrees = 270f,
            neighborsRadius = 2.5f,
            personalRadius = 0.7f,
            cruiseSpeed = 4f,
            minSpeed = 2.5f,
            maxSpeed = 6f,
            speedMatchWeight = 0.4f,
            catchUpGain = 2f,
            speedResponse = 3f
        };
    }

    private static SimulationConfigData TightSchool()
    {
        return new SimulationConfigData
        {
            alignmentWeight = 2f,
            separationWeight = 2f,
            cohesionWeight = 1.5f,
            edgeWeight = 3f,
            turnRate = 8f,
            fieldOfViewDegrees = 300f,
            neighborsRadius = 1.5f,
            personalRadius = 0.5f,
            cruiseSpeed = 4f,
            minSpeed = 3f,
            maxSpeed = 5.5f,
            speedMatchWeight = 0.7f,
            catchUpGain = 2f,
            speedResponse = 4f
        };
    }

    private static SimulationConfigData Chaotic()
    {
        return new SimulationConfigData
        {
            alignmentWeight = 0.3f,
            separationWeight = 2.5f,
            cohesionWeight = 0.5f,
            edgeWeight = 3f,
            turnRate = 12f,
            fieldOfViewDegrees = 200f,
            neighborsRadius = 1.5f,
            personalRadius = 0.8f,
            cruiseSpeed = 5f,
            minSpeed = 2.5f,
            maxSpeed = 8f,
            speedMatchWeight = 0f,
            catchUpGain = 3f,
            speedResponse = 2f
        };
    }

    private static SimulationConfigData WithBehavior(SimulationConfigData target, SimulationConfigData source)
    {
        target.alignmentWeight = source.alignmentWeight;
        target.separationWeight = source.separationWeight;
        target.cohesionWeight = source.cohesionWeight;
        target.edgeWeight = source.edgeWeight;
        target.turnRate = source.turnRate;
        target.fieldOfViewDegrees = source.fieldOfViewDegrees;
        target.neighborsRadius = source.neighborsRadius;
        target.personalRadius = source.personalRadius;
        target.cruiseSpeed = source.cruiseSpeed;
        target.minSpeed = source.minSpeed;
        target.maxSpeed = source.maxSpeed;
        target.speedMatchWeight = source.speedMatchWeight;
        target.catchUpGain = source.catchUpGain;
        target.speedResponse = source.speedResponse;

        return target;
    }
}
