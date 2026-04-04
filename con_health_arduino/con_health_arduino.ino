#include <Wire.h>
#include <Adafruit_GFX.h>
#include <Adafruit_SSD1306.h>
#include "MAX30105.h"
#include "heartRate.h"
#include <WiFi.h>
#include <PubSubClient.h>
#include <ArduinoJson.h>

#define SCREEN_WIDTH 128
#define SCREEN_HEIGHT 64
Adafruit_SSD1306 display(SCREEN_WIDTH, SCREEN_HEIGHT, &Wire, -1);

const char* ssid = "Katia"; 
const char* password = "66666666"; 
const char* mqtt_server = "broker.emqx.io";
const char* topic_monitor = "smarthealth/patient/monitor";

#define MPU6050_ADDR 0x68
#define ACCEL_SCALE 16384.0f
#define FREE_FALL_THRESHOLD 0.5f
#define IMPACT_THRESHOLD 1.2f
#define IMMOBILITY_THRESHOLD 5.0f
#define FREE_FALL_DURATION_MS 20
#define IMPACT_DURATION_MS 2000
#define IMMOBILITY_DURATION_MS 20

typedef enum { STATE_NORMAL, STATE_FREE_FALL, STATE_IMPACT, STATE_IMMOBILITY, STATE_FALL_DETECTED } fall_state_t;

WiFiClient espClient;
PubSubClient client(espClient);
MAX30105 particleSensor;

// --- DỮ LIỆU CẢM BIẾN (GIỮ NGUYÊN BỘ LỌC ĐỂ CHUẨN) ---
const byte RATE_SIZE = 2; 
byte rates[RATE_SIZE]; 
byte rateSpot = 0;
long lastBeat = 0; 
int beatAvg = 0;
int spo2Value = 0; 
char device_id[18];

fall_state_t fall_state = STATE_NORMAL;
uint32_t state_start_time = 0;
float last_accel_magnitude = 1.0f;
bool fall_detected_flag = false;
bool alert_sent = false; 

void get_device_id() {
 uint8_t mac[6];
 WiFi.macAddress(mac);
 sprintf(device_id, "%02X:%02X:%02X:%02X:%02X:%02X", mac[0], mac[1], mac[2], mac[3], mac[4], mac[5]);
}

const char* get_state_name(fall_state_t state) {
 switch (state) {
  case STATE_NORMAL: return "Normal";
  case STATE_FREE_FALL: return "Free Fall";
  case STATE_IMPACT: return "Impact";
  case STATE_IMMOBILITY: return "Immobility";
  case STATE_FALL_DETECTED: return "FALL DETECTED";
  default: return "Unknown";
 }
}

void setup() {
 Serial.begin(115200);
 delay(2000);
 Wire.begin(21, 22);
 get_device_id();

 if(!display.begin(SSD1306_SWITCHCAPVCC, 0x3C)) Serial.println("OLED Fail");
 display.clearDisplay();
 display.setTextColor(WHITE);
 display.setTextSize(1);
 
 WiFi.mode(WIFI_STA); 
 WiFi.begin(ssid, password);

if (!particleSensor.begin(Wire, I2C_SPEED_STANDARD)) { 
 Serial.println("MAX30105 không phản hồi!");
 while (1); 
}
 particleSensor.setup(0x3F, 4, 2, 411, 411, 16384);
 
 Wire.beginTransmission(MPU6050_ADDR);
 Wire.write(0x6B); Wire.write(0x00); 
 Wire.endTransmission();

 client.setServer(mqtt_server, 1883);
}

void loop() {
 // --- KIỂM TRA KẾT NỐI WIFI ---
 if (WiFi.status() != WL_CONNECTED) {
  static unsigned long lastWifiRetry = 0;
  if (millis() - lastWifiRetry > 5000) {
   lastWifiRetry = millis();
   WiFi.begin(ssid, password);
  }
  
  display.clearDisplay();
  display.setCursor(0, 25);
  display.println("Connecting...");
  display.display();
  return; 
 }


 // --- KIỂM TRA KẾT NỐI MQTT ---
 if (!client.connected()) {
  static unsigned long lastMqttRetry = 0;
  if (millis() - lastMqttRetry > 5000) {
   lastMqttRetry = millis();
   String clientId = "ESP32-" + String(device_id);
   client.connect(clientId.c_str());
  }
 }
 client.loop();

 // --- LOGIC ĐO ĐẠC ---
 uint32_t now = millis();
 
 // 1. PHÁT HIỆN NGÃ 
 Wire.beginTransmission(MPU6050_ADDR);
 Wire.write(0x3B);
 Wire.endTransmission(false);
 Wire.requestFrom(MPU6050_ADDR, 6, true);
 if (Wire.available() >= 6) {
  int16_t raw_ax = Wire.read() << 8 | Wire.read();
  int16_t raw_ay = Wire.read() << 8 | Wire.read();
  int16_t raw_az = Wire.read() << 8 | Wire.read();
  float mag = sqrt(pow(raw_ax/ACCEL_SCALE,2) + pow(raw_ay/ACCEL_SCALE,2) + pow(raw_az/ACCEL_SCALE,2));
  uint32_t duration = now - state_start_time;
  switch (fall_state) {
   case STATE_NORMAL: if (mag < FREE_FALL_THRESHOLD) { fall_state = STATE_FREE_FALL; state_start_time = now; } break;
   case STATE_FREE_FALL: if (mag < FREE_FALL_THRESHOLD) { if (duration >= FREE_FALL_DURATION_MS) { fall_state = STATE_IMPACT; state_start_time = now; } } else fall_state = STATE_NORMAL; break;
   case STATE_IMPACT: if (mag > IMPACT_THRESHOLD) { fall_state = STATE_IMMOBILITY; state_start_time = now; } else if (duration > IMPACT_DURATION_MS) fall_state = STATE_NORMAL; break;
   case STATE_IMMOBILITY: if (abs(mag - last_accel_magnitude) < IMMOBILITY_THRESHOLD) { if (duration >= IMMOBILITY_DURATION_MS) { fall_state = STATE_FALL_DETECTED; fall_detected_flag = true; } } else fall_state = STATE_NORMAL; break;
   case STATE_FALL_DETECTED: if (duration > 15000) { fall_detected_flag = false; fall_state = STATE_NORMAL; } break;
  }
  last_accel_magnitude = mag;
 }

 // 2. NHỊP TIM (LỌC TRUNG BÌNH TRƯỢT)
 long irValue = particleSensor.getIR();
 if (irValue > 25000) {
  if (checkForBeat(irValue) == true) {
   long delta = millis() - lastBeat;
   lastBeat = millis();
   float bpm = 60 / (delta / 1000.0);
   if (bpm < 180 && bpm > 50) {
    rates[rateSpot++] = (byte)bpm;
    rateSpot %= RATE_SIZE;
    beatAvg = 0;
    for (byte x = 0; x < RATE_SIZE; x++) beatAvg += rates[x];
    beatAvg /= RATE_SIZE;
   }
  }
  float currentSpo2 = 110 - 15 * ((float)particleSensor.getRed() / irValue);
  if (currentSpo2 > 100) currentSpo2 = 100;
  spo2Value = (0.5 * spo2Value) + (0.5 * currentSpo2);
 } else {
  beatAvg = 0; spo2Value = 0; lastBeat = 0;
  for(byte i=0; i < RATE_SIZE; i++) rates[i] = 0;
 }

 // 3. HIỂN THỊ
 static long lastDisp = 0;
 if (millis() - lastDisp > 500) {
  lastDisp = millis();
  display.clearDisplay();
  display.setCursor(0,0);
  if (fall_detected_flag) {
   display.setTextSize(2); display.println("!! FALL !!");
  } else {
   display.setTextSize(1); 
   display.print("BPM: "); display.println(beatAvg);
   display.print("SpO2: "); display.print(spo2Value); display.println("%");
   display.print("State: "); display.println(get_state_name(fall_state));
  }
  display.display();
 }

 // 4. GỬI MQTT
 static long lastM = 0;
 if (millis() - lastM > 2000 && client.connected()) {
  lastM = millis();
  
  // Nếu đang trong trạng thái NGÃ và CHƯA gửi cảnh báo
  if (fall_state == STATE_FALL_DETECTED) {
   if (!alert_sent) { 
    StaticJsonDocument<256> doc;
    doc["bpm"] = beatAvg; 
    doc["spo2"] = spo2Value;
    doc["status"] = "FALL DETECTED"; 
    
    char buffer[256];
    serializeJson(doc, buffer);
    client.publish(topic_monitor, buffer);
    
    alert_sent = true; 
    Serial.println("Sent Fall Alert (Once)!");
   }
  } 
  // Nếu trạng thái bình thường (Normal), gửi dữ liệu định kỳ và reset chốt
  else if (fall_state == STATE_NORMAL) {
   StaticJsonDocument<256> doc;
   doc["bpm"] = beatAvg; 
   doc["spo2"] = spo2Value;
   doc["status"] = "Normal"; 
   
   char buffer[256];
   serializeJson(doc, buffer);
   client.publish(topic_monitor, buffer);
   
   alert_sent = false; 
  }
 }
}
