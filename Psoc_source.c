/* ========================================
 * Scalemonitor
 *
 * ========================================
*/
/* Auto ranging, variable precision and maximum weight algorithm:
ScaledRange = (MaxAvgReading – MinAvgReading)/MaximumWeight;

Do{
    ADCin = ADCin – MinAvgReading;  //read the proper ADC based on ADCselect

    if (ADCin < 0)
	ADCin= 0;

    Digit[0] = int(ADCin/ScaledRange);  //note the integer is in the AX register after div
    Remainder = ADCin MOD ScaledRange;  
   for (i=1; i< DigitsPrecision; i++)
   {
      Digit[i] = (Remainder*10)/ScaledRange;  //this is the integer result
      Remainder = (Remainder*10) MOD ScaledRange; //(this is the remainder register from the previous division)
   }

   LastFractionResult = (Remainder * 10)/ScaledRange;

   if (LastFractionResult >=5 )
      Digit[i-1] =   Digit[i-1] + 1;		//round up last digit

   //Convert digits to ascii and save in out_buffer
}while(1);  

*/


#include <device.h>
/* USB configuration constants */
#define IN_EP       (0x01u)
#define OUT_EP      (0x02u)
#define BUF_SIZE    (0x40u)

// Scale constants
//mode byte mask bits
#define INIT_SCALE_VARS (0x01u)
#define GET_SCALE_LOW_LIMIT (0x02u)
#define GET_SCALE_HIGH_LIMIT (0x04u)
#define SEND_SCALE_WEIGHT (0x08u)
#define GET_CUSTOM_DATA (0x10u)

//var byte mask bits
#define MAX_WEIGHT_VAR_MASK_BIT   (0x01u)
#define PRECISION_VAR_MASK_BIT  (0x02u)
#define AUTO_MODE_MASK_BIT  (0x04u)  //low is fixed , high is auto
#define CONTINOUS_MODE_MASK_BIT (0x08u) //low is single send, high is continuous

//  in buffer array byte values
#define MODE_BYTE (0x00u)
#define SET_VARS_MASK_BYTE (0x01u)
#define MAX_WEIGHT_VAR_BYTE (0x02u)
#define PRECISION_VAR_BYTE (0x03u)
#define CUSTOM_DATA_START (0x08u)
#define CUSTOM_DATA_MAX_LENGTH (0x20u)

#define TRUE 1u
#define FALSE 0u
#define ADC_MSB (0x40)
#define ADC_7LSBs (0x3f)
#define NUMBER_SAMPLES 100u
#define MIN_PRECISION_DIGITS 1
#define MAX_PRECISION_DIGITS 5
#define MIN_WEIGHT_SCALE 1
#define MAX_WEIGHT_SCALE 5
uint8 in_buffer[BUF_SIZE];
uint8 out_buffer[BUF_SIZE];
uint8 length;

uint8 adc_flag;

/* Fixed two digit precision max value is 1.99 algorithm:
extract bit7 it is either 1 or 0, the low 7 bits are fraction calculated as:
Decimal Fraction = (7bit-LSBs* 99 + 64)/127  (this is a word wide division calculation)
Finally separate the values into 2 BCD values with a byte wide divide by 10. 

*/
void calculate_fixed_weight(uint8 adc_data, uint8 *weight_buffer)
{   uint16 fraction;
    weight_buffer[0]=(adc_data & ADC_MSB) >> 6; //shift bit to lsb
    weight_buffer[0] += (0x30); //convert to ascii
    weight_buffer[1]='.';
   
    adc_data &= ADC_7LSBs; //extract 7 lsbs of fraction
    fraction = (adc_data * 99 + 32)/63;
    weight_buffer[3]= fraction % 10 + (0x30); //get lsd convert to ascii
    fraction /= 10; //isolate last digit
    weight_buffer[2]= fraction + (0x30); //convert to ascii
}

void calculate_scaled_weight(uint8 adc_data, uint8 *out_buffer, uint16 scaled_range)
{
  
  int i = 0;

  adc_data = adc_data - MinAvgReading;
  if(adc_data < 0)
    adc_data = 0;

  out_buffer[0] = int(adc_data/ScaledRange);
  out_buffer[0] += (0x30); // Convert to ascii 
  out_buffer[1] = '.';

  int rem = adc_data % ScaledRange;
  for(i = 2; i < DigitsPrecision; i ++){
    out_buffer[i] = (rem * 10) / scaledRange;
    out_buffer[i] += (0x30); // Convert to ascii
    rem = (rem * 10) % ScaledRange;
  }

 
  // Round 
  int LastFractionResult = (rem * 10) / ScaledRange;
  if(LastFractionResult >= 5){
    out_buffer[i-1] = out_buffer[i-1] + 1; 
  }


}



uint8 get_adc_average(uint8 samples)
{ 
    uint8 i=0,ADC_in;
    uint16 AvgReading;
    
    do{
       //this flag tests if the ADC interrupt occurred meaning data is ready 
       if(adc_flag==1u)
       {
          ADC_in=ADC_DelSig_1_GetResult8(); //read adc byte 
          AvgReading += ADC_in;
          i++;
       }                      
    }while(i<samples);
    
    AvgReading /=samples;
    return AvgReading;
}

void SendOutData(uint8 buffer[])
{
   if(USBFS_1_GetEPState(IN_EP) != USBFS_1_IN_BUFFER_EMPTY);
   {
      while(USBFS_1_GetEPState(IN_EP) != USBFS_1_IN_BUFFER_EMPTY);
      /* Load the IN buffer (this requires host request)*/
      USBFS_1_LoadInEP(IN_EP, &buffer[0u], BUF_SIZE);
   }
}

void clear_buffer(uint8 *buffer, uint8 length)
{   int i;
    for(i=0;i<length;i++)
        buffer[i]=0;
}

void main()
{
    uint8 state, var_mask, ADC_in, auto_mode, continous_mode;
    uint8  DigitsPrecision, MaximumWeight;
    int16 MaxAvgReading = null, \
          MinAvgReading = null, \ 
          ScaledRange = null; 
  //uint8 Digit[5], ScaledRange, LastFractionResult, Remainder;

    
    /* Start the components */
    ADC_DelSig_1_Start();
    //ADC_DelSig_1_IRQ_Start();  //disable for manual polling instead so can single step
    
    CYGlobalIntEnable;   //enable for ADC and USB interrupts     

    
    /* Start the ADC conversion */
    ADC_DelSig_1_StartConvert();
    ADC_DelSig_1_IRQ_Disable();  //disable for manual polling instead so can single step
    
    
    /* Start USBFS Operation with 5V operation */
    USBFS_1_Start(0u, USBFS_1_5V_OPERATION); //USBFS_1_5V_OPERATION);

    /* Wait for Device to enumerate i.e. detects USB settings from host PC */
    while(USBFS_1_GetConfiguration() != 0u);

    /* Enumeration is done, enable OUT endpoint for receive data from Host */
    USBFS_1_EnableOutEP(OUT_EP);
    
    state=0u;
    for(;;) // this is infinite state loop
    { 
        if(state==0u)
        {
           /* Check that configuration is changed (there is only one configured on the USBFS )*/
           if(USBFS_1_IsConfigurationChanged() != 0u)
           {
              /* Re-enable endpoint when device is configured */
              if(USBFS_1_GetConfiguration() != 0u)
              {
                 USBFS_1_EnableOutEP(OUT_EP);
              }
           }
        
           /* GET MODE STATE Read USB PC host application*/
           if(USBFS_1_GetEPState(OUT_EP) == USBFS_1_OUT_BUFFER_FULL)
           {
              /* Read received bytes count */
              length = USBFS_1_GetEPCount(OUT_EP);

              /* Unload the OUT buffer */
              USBFS_1_ReadOutEP(OUT_EP, &in_buffer[0u], length);   
              state= in_buffer[MODE_BYTE];
              var_mask=in_buffer[SET_VARS_MASK_BYTE];

              if(var_mask & CONTINOUS_MODE_MASK_BIT)
                continous_mode = TRUE;
              else
                continous_mode = FALSE;
            }
        }
        
        if(state & INIT_SCALE_VARS)
        {
            if(var_mask & MAX_WEIGHT_VAR_MASK_BIT)
                MaximumWeight=in_buffer[MAX_WEIGHT_VAR_BYTE];
            if(MaximumWeight<MIN_WEIGHT_SCALE)
                MaximumWeight=MIN_WEIGHT_SCALE;
            if(MaximumWeight>MAX_WEIGHT_SCALE)
                MaximumWeight=MAX_WEIGHT_SCALE;           
            if(var_mask & PRECISION_VAR_MASK_BIT)
                DigitsPrecision=in_buffer[PRECISION_VAR_BYTE];
            if(DigitsPrecision<MIN_PRECISION_DIGITS)
                DigitsPrecision=MIN_PRECISION_DIGITS;
            if(DigitsPrecision>MAX_PRECISION_DIGITS)
                DigitsPrecision=MAX_PRECISION_DIGITS;
            if(var_mask & AUTO_MODE_MASK_BIT)
                auto_mode=TRUE;
            else
                auto_mode=FALSE;
            
            state=0u;
        }
         
        if(state & GET_SCALE_LOW_LIMIT)
        {             
            MinAvgReading = get_adc_average(NUMBER_SAMPLES);
            clear_buffer(out_buffer,BUF_SIZE);
            out_buffer[0u]=(uint8)MinAvgReading;
            //respond with min avg adc value (this requires host request)                       
            SendOutData(out_buffer); 
            state=0u;
        }
        
        if(state & GET_SCALE_HIGH_LIMIT)
        {   
            MaxAvgReading = get_adc_average(NUMBER_SAMPLES);            
            clear_buffer(out_buffer,BUF_SIZE);
            out_buffer[0u]=(uint8)MaxAvgReading;
            //respond with max avg adc value (this requires host request)                      
            SendOutData(out_buffer);  
            state=0u;
        }        
        
        if(state & SEND_SCALE_WEIGHT)
        {   
           do
           {
                //this flag tests if the ADC interrupt occurred meaning data is ready 
                 
                adc_flag=Status_Reg_1_Read();  //note the interrupt is disabled so read EOC bit instead
                if(adc_flag==1u)
                {
                    ADC_in=ADC_DelSig_1_GetResult8(); //read adc byte
                    adc_flag=0u;
                    
                    clear_buffer(out_buffer,BUF_SIZE);
                    
                    if(auto_mode==FALSE)
                        calculate_fixed_weight(ADC_in, out_buffer);
                    if(auto_mode==TRUE)
                        if( (MaxAvgReading == null) || (MinAvgReading == null) )
                          // Send Error Message
                          continue;
                        ScaledRange = (MaxAvgReading - MinAvgReading) / MaximumWeight;
                        calculate_scaled_weight(ADC_in, out_buffer, ScaledRange);
                    
                    //send weight data (this requires host request)                       
                    SendOutData(out_buffer);
                
                              
                }
            }while(USBFS_1_GetEPState(OUT_EP) != USBFS_1_OUT_BUFFER_FULL \
                   && continous_mode == TRUE);
            
        state=0u;  

        }

 
        

    }
}

/* [] END OF FILE */
