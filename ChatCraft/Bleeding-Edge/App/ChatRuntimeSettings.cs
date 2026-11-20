using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using System.Windows.Media;

// ChatCraft-owned controls and preferences; WordCraft settings remain independent.
sealed partial class ChatCraftWindow
{
    readonly NoiseGenerator backgroundNoise = new NoiseGenerator();
    MorseOutput backgroundOutput;
    Dictionary<string,double> audioValues = new Dictionary<string,double>();
    bool audioSaveBlocked;
    internal void SyncBackgroundSound()
    {
        backgroundNoise.Master=morse.Volume;
        backgroundNoise.Suspended=closed || WindowState==WindowState.Minimized;
        backgroundNoise.Playing=!closed && ReplyOpen && flow!=null && flow.Reader.PlaybackDirection!=0
            && !scrubbingReply && colorPanel==null;
    }
    internal void SetMasterVolume(double value)
    {
        morse.Volume=FiniteGain(value);volumeDirty=true;SyncBackgroundSound();
    }
    static double FiniteGain(double value)
    {return Double.IsNaN(value)||Double.IsInfinity(value)?0:Math.Max(0,Math.Min(1,value));}
    void LoadAudioValues()
    {
        if(!File.Exists(volumeSettingsPath))return;
        try {
            var values=new JavaScriptSerializer().Deserialize<Dictionary<string,double>>(File.ReadAllText(volumeSettingsPath));
            if(values==null)throw new FormatException("Expected audio preferences.");
            foreach(var v in values.Values)if(Double.IsNaN(v)||Double.IsInfinity(v))throw new FormatException("Invalid audio value.");
            audioValues=values;
            double n;
            if(values.TryGetValue("volume",out n))morse.Volume=FiniteGain(n);
            if(values.TryGetValue("noiseVolume",out n))backgroundNoise.Level=FiniteGain(n);
            if(values.TryGetValue("noiseKind",out n))backgroundNoise.Kind=(int)Math.Max(0,Math.Min(2,n));
            if(values.TryGetValue("secondaryNoiseVolume",out n))backgroundNoise.SecondaryLevel=FiniteGain(n);
            if(values.TryGetValue("secondaryNoiseKind",out n))backgroundNoise.SecondaryKind=(int)Math.Max(0,Math.Min(2,n));
            if(values.TryGetValue("noisePlaybackOnly",out n))backgroundNoise.PlaybackOnly=n!=0;
            if(values.TryGetValue("noiseSecondaryWhileReading",out n))backgroundNoise.SecondaryWhileReading=n!=0;
        } catch(Exception e) {
            if(!(e is IOException || e is UnauthorizedAccessException || e is ArgumentException || e is InvalidOperationException || e is FormatException))throw;
            try {File.Copy(volumeSettingsPath,volumeSettingsPath+"."+DateTime.UtcNow.ToString("yyyyMMdd-HHmmssfff")+".recovery");}
            catch(IOException){audioSaveBlocked=true;}
            catch(UnauthorizedAccessException){audioSaveBlocked=true;}
        }
        SyncBackgroundSound();
    }
    bool SaveAudioValues()
    {
        if(audioSaveBlocked)return false;
        try {
            audioValues["volume"]=morse.Volume;
            audioValues["noiseVolume"]=backgroundNoise.Level;audioValues["noiseKind"]=backgroundNoise.Kind;
            audioValues["secondaryNoiseVolume"]=backgroundNoise.SecondaryLevel;audioValues["secondaryNoiseKind"]=backgroundNoise.SecondaryKind;
            audioValues["noisePlaybackOnly"]=backgroundNoise.PlaybackOnly?1:0;
            audioValues["noiseSecondaryWhileReading"]=backgroundNoise.SecondaryWhileReading?1:0;
            audioValues["noisePalette"]=2;
            string temp=volumeSettingsPath+".tmp";
            File.WriteAllText(temp,new JavaScriptSerializer().Serialize(audioValues));
            if(File.Exists(volumeSettingsPath))File.Replace(temp,volumeSettingsPath,null);else File.Move(temp,volumeSettingsPath);
            volumeDirty=false;return true;
        } catch(IOException){return false;}
        catch(UnauthorizedAccessException){return false;}
    }
    FrameworkElement BuildRuntimeControls()
    {
        var body=new StackPanel();
        body.Children.Add(new TextBlock {Text="Sound",FontSize=20,Margin=new Thickness(0,8,0,5)});
        AddGain(body,"Sound volume",morse.Volume,SetMasterVolume);
        AddChoices(body,"Primary background sound",backgroundNoise.Kind,n=>{backgroundNoise.Kind=n;volumeDirty=true;});
        AddGain(body,"Primary background volume",backgroundNoise.Level,n=>{backgroundNoise.Level=n;volumeDirty=true;});
        AddCheck(body,"Primary only during playback",backgroundNoise.PlaybackOnly,n=>{backgroundNoise.PlaybackOnly=n;volumeDirty=true;SyncBackgroundSound();});
        AddChoices(body,"Secondary background sound",backgroundNoise.SecondaryKind,n=>{backgroundNoise.SecondaryKind=n;volumeDirty=true;});
        AddGain(body,"Secondary background volume",backgroundNoise.SecondaryLevel,n=>{backgroundNoise.SecondaryLevel=n;volumeDirty=true;});
        AddCheck(body,"Add secondary sound while reading",backgroundNoise.SecondaryWhileReading,n=>{backgroundNoise.SecondaryWhileReading=n;volumeDirty=true;SyncBackgroundSound();});
        body.Children.Add(new TextBlock {Text="Graphics quality",FontSize=20,Margin=new Thickness(0,20,0,5)});
        var quality=new Slider {Minimum=0,Maximum=3,TickFrequency=1,IsSnapToTickEnabled=true,Value=(int)GpuQuality.Tier};
        AutomationProperties.SetName(quality,"Graphics quality");
        var caption=new TextBlock {TextWrapping=TextWrapping.Wrap,Opacity=.75,Margin=new Thickness(0,3,0,10)};
        Action refresh=()=>{int q=(int)quality.Value;caption.Text=new[]{"Low · fastest","Medium · balanced","High · detailed","Ultra · most detail"}[q]
            +"\n"+new[]{144,288,432,576}[q]+" simulation rows · up to "+GpuQuality.Rows((QualityTier)q)+" picture rows. Reading text stays sharp.";};
        quality.ValueChanged+=(s,e)=>{scene.SetGraphicsQuality((QualityTier)(int)quality.Value);refresh();};
        body.Children.Add(quality);body.Children.Add(caption);refresh();
        body.Children.Add(new TextBlock {Text="Lower quality reduces GPU work. Reading speed is separate.",TextWrapping=TextWrapping.Wrap,Opacity=.65,Margin=new Thickness(0,0,0,16)});
        SyncBackgroundSound();return body;
    }
    static void AddGain(Panel body,string label,double initial,Action<double> change)
    {
        var text=new TextBlock {Text=label,Margin=new Thickness(0,8,0,2)};
        var value=new TextBlock {Opacity=.7};
        var slider=new Slider {Minimum=0,Maximum=100,TickFrequency=1,IsSnapToTickEnabled=true,Value=initial*100};
        AutomationProperties.SetName(slider,label);
        value.Text=Math.Round(slider.Value)+"%";
        slider.ValueChanged+=(s,e)=>{change(slider.Value/100);value.Text=Math.Round(slider.Value)+"%";};
        body.Children.Add(text);body.Children.Add(slider);body.Children.Add(value);
    }
    static void AddChoices(Panel body,string label,int initial,Action<int> change)
    {
        body.Children.Add(new TextBlock {Text=label,Margin=new Thickness(0,9,0,3)});
        var combo=new ComboBox {Foreground=Brushes.Black,ItemsSource=new[]{"Dark noise","Darker noise","Darkest noise"},SelectedIndex=initial};
        AutomationProperties.SetName(combo,label);combo.SelectionChanged+=(s,e)=>change(combo.SelectedIndex);body.Children.Add(combo);
    }
    static void AddCheck(Panel body,string label,bool initial,Action<bool> change)
    {
        var check=new CheckBox {Content=label,IsChecked=initial,Margin=new Thickness(0,8,0,3),Foreground=Brushes.LightSteelBlue};
        check.Checked+=(s,e)=>change(true);check.Unchecked+=(s,e)=>change(false);body.Children.Add(check);
    }
}

// Independent filtered-random streams: the reading layer adds to the primary.
sealed class NoiseGenerator
{
    readonly object gate=new object();
    readonly DarkNoiseLayer primary=new DarkNoiseLayer(0x937a451bu);
    readonly DarkNoiseLayer secondary=new DarkNoiseLayer(0x34e916c7u);
    double level=.15,secondaryLevel=.15,master=1;
    double primaryAmplitude,secondaryAmplitude;
    int kind,secondaryKind=1;
    bool playing,playbackOnly=true,suspended,secondaryWhileReading;
    internal double Level {get{lock(gate)return level;}set{lock(gate)level=value;}}
    internal double SecondaryLevel {get{lock(gate)return secondaryLevel;}set{lock(gate)secondaryLevel=value;}}
    internal double Master {get{lock(gate)return master;}set{lock(gate)master=value;}}
    internal int Kind {get{lock(gate)return kind;}set{lock(gate)kind=value;}}
    internal int SecondaryKind {get{lock(gate)return secondaryKind;}set{lock(gate)secondaryKind=value;}}
    internal bool SecondaryWhileReading {get{lock(gate)return secondaryWhileReading;}set{lock(gate)secondaryWhileReading=value;}}
    internal bool Playing {get{lock(gate)return playing;}set{lock(gate)playing=value;}}
    internal bool PlaybackOnly {get{lock(gate)return playbackOnly;}set{lock(gate)playbackOnly=value;}}
    internal bool Suspended {get{lock(gate)return suspended;}set{lock(gate)suspended=value;}}
    internal void Fill(short[] samples)
    {
        double primaryTarget,secondaryTarget;int first,second;
        lock(gate){
            primaryTarget=!suspended&&(!playbackOnly||playing)?master*level:0;
            secondaryTarget=!suspended&&playing&&secondaryWhileReading?master*secondaryLevel:0;
            first=kind;second=secondaryKind;
        }
        double rise=1-Math.Exp(-1.0/(MorseOutput.SampleRate*.12));
        double fall=1-Math.Exp(-1.0/(MorseOutput.SampleRate*.40));
        for(int i=0;i<samples.Length;i++){
            primaryAmplitude+=(primaryTarget-primaryAmplitude)*(primaryTarget>primaryAmplitude?rise:fall);
            secondaryAmplitude+=(secondaryTarget-secondaryAmplitude)*(secondaryTarget>secondaryAmplitude?rise:fall);
            if(primaryTarget==0&&primaryAmplitude<1e-6)primaryAmplitude=0;
            if(secondaryTarget==0&&secondaryAmplitude<1e-6)secondaryAmplitude=0;
            double value=.24*(primary.Sample(first)*primaryAmplitude+secondary.Sample(second)*secondaryAmplitude);
            samples[i]=(short)Math.Round(32767*value);
        }
    }
}

sealed class DarkNoiseLayer
{
    static readonly double[] alpha={.035,.015,.006};
    static readonly double[] normalize={Math.Sqrt((2-.035)/.035),Math.Sqrt((2-.015)/.015),Math.Sqrt((2-.006)/.006)};
    static readonly double color=1-Math.Exp(-1.0/(MorseOutput.SampleRate*.08));
    readonly double[] filtered=new double[3],weights={1,0,0};
    uint random;
    internal DarkNoiseLayer(uint seed){random=seed;}
    internal double Sample(int kind)
    {
        random^=random<<13;random^=random>>17;random^=random<<5;
        double white=(random>>8)/8388608.0-1,value=0;
        for(int j=0;j<3;j++){
            filtered[j]+=alpha[j]*(white-filtered[j]);
            weights[j]+=color*((kind==j?1:0)-weights[j]);
            value+=weights[j]*filtered[j]*normalize[j];
        }
        return Math.Tanh(value);
    }
}
