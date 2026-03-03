using System.ComponentModel;

namespace Agent.Tools
{
    public class WeatherForecast
    {
        public WeatherForecast()
        {

        }

        [Description("Get the weather for a given location.")]
        public string GetWeather([Description("The location to get the weather for.")] string location)
        {
            switch (random_.Next(3))
            {
                case 0:
                    return $"The weather in {location} is sunny with a high of 25°C.";
                case 1:
                    return $"The weather in {location} is rainy with a high of 20°C.";
                default:
                    return $"The weather in {location} is cloudy with a high of 15°C.";
            }
        }

        private Random random_ = new Random();
    }
}
