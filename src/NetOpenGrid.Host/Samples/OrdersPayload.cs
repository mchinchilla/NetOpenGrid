namespace NetOpenGrid.Host.Samples;

public static class OrdersPayload
{
    public const string Json = """
        [
          {"id":"ORD-1001","customer":"Acme Corp","product":"Mechanical Keyboard","amount":149.99,"status":"delivered","placedAt":"2026-07-02"},
          {"id":"ORD-1002","customer":"Globex","product":"4K Monitor 27in","amount":389.00,"status":"shipped","placedAt":"2026-07-05"},
          {"id":"ORD-1003","customer":"Initech","product":"Ergonomic Mouse","amount":79.50,"status":"pending","placedAt":"2026-07-09"},
          {"id":"ORD-1004","customer":"Umbrella Ltd","product":"USB-C Dock","amount":219.90,"status":"cancelled","placedAt":"2026-07-11"},
          {"id":"ORD-1005","customer":"Acme Corp","product":"Laptop Stand","amount":59.00,"status":"delivered","placedAt":"2026-07-12"},
          {"id":"ORD-1006","customer":"Stark Industries","product":"Webcam 1080p","amount":99.95,"status":"shipped","placedAt":"2026-07-14"},
          {"id":"ORD-1007","customer":"Wayne Enterprises","product":"Noise Cancelling Headset","amount":249.00,"status":"pending","placedAt":"2026-07-15"},
          {"id":"ORD-1008","customer":"Globex","product":"Standing Desk Converter","amount":289.00,"status":"delivered","placedAt":"2026-07-18"},
          {"id":"ORD-1009","customer":"Initech","product":"Cable Management Kit","amount":24.99,"status":"delivered","placedAt":"2026-07-20"},
          {"id":"ORD-1010","customer":"Hooli","product":"Portable SSD 1TB","amount":129.99,"status":"shipped","placedAt":"2026-07-21"},
          {"id":"ORD-1011","customer":"Umbrella Ltd","product":"Monitor Light Bar","amount":89.00,"status":"pending","placedAt":"2026-07-23"},
          {"id":"ORD-1012","customer":"Stark Industries","product":"Wireless Charger Pad","amount":39.99,"status":"cancelled","placedAt":"2026-07-25"},
          {"id":"ORD-1013","customer":"Wayne Enterprises","product":"External GPU Case","amount":459.00,"status":"shipped","placedAt":"2026-07-27"},
          {"id":"ORD-1014","customer":"Hooli","product":"HDMI 2.1 Cable 2m","amount":19.99,"status":"delivered","placedAt":"2026-07-29"},
          {"id":"ORD-1015","customer":"Acme Corp","product":"Trackball Mouse","amount":109.00,"status":"pending","placedAt":"2026-08-01"},
          {"id":"ORD-1016","customer":"Globex","product":"Desk Mat XXL","amount":35.00,"status":"delivered","placedAt":"2026-08-02"},
          {"id":"ORD-1017","customer":"Initech","product":"Streaming Microphone","amount":159.00,"status":"shipped","placedAt":"2026-08-04"},
          {"id":"ORD-1018","customer":"Umbrella Ltd","product":"Ring Light 10in","amount":49.50,"status":"delivered","placedAt":"2026-08-06"},
          {"id":"ORD-1019","customer":"Stark Industries","product":"Tablet Stylus Pro","amount":119.00,"status":"pending","placedAt":"2026-08-08"},
          {"id":"ORD-1020","customer":"Wayne Enterprises","product":"Smart Speaker Mini","amount":69.00,"status":"cancelled","placedAt":"2026-08-09"},
          {"id":"ORD-1021","customer":"Hooli","product":"Fingerprint Reader USB","amount":54.99,"status":"delivered","placedAt":"2026-08-11"},
          {"id":"ORD-1022","customer":"Acme Corp","product":"Network Switch 8-port","amount":94.00,"status":"shipped","placedAt":"2026-08-13"},
          {"id":"ORD-1023","customer":"Globex","product":"Laptop Sleeve 14in","amount":29.99,"status":"delivered","placedAt":"2026-08-14"},
          {"id":"ORD-1024","customer":"Initech","product":"Vertical Laptop Stand","amount":42.00,"status":"pending","placedAt":"2026-08-16"},
          {"id":"ORD-1025","customer":"Umbrella Ltd","product":"Monitor Arm Dual","amount":179.00,"status":"shipped","placedAt":"2026-08-17"},
          {"id":"ORD-1026","customer":"Stark Industries","product":"Mechanical Numpad","amount":74.90,"status":"delivered","placedAt":"2026-08-18"},
          {"id":"ORD-1027","customer":"Wayne Enterprises","product":"Air Quality Sensor","amount":199.00,"status":"pending","placedAt":"2026-08-19"},
          {"id":"ORD-1028","customer":"Hooli","product":"Fast GaN Charger 65W","amount":44.00,"status":"shipped","placedAt":"2026-08-20"}
        ]
        """;
}
