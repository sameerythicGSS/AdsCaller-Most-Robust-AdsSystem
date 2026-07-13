using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

public class PanelAdTreatment_Salon : MonoBehaviour
{
    public EnumsJar_Salon.ToDo treatBanner;
    public EnumsJar_Salon.ToDo treatLargeBanner;

    public bool showInterstitial;

    public float delay = 1;

    [Header("Independent Disable Treatments")]
    public bool invertBannerOnDisable;
    public bool invertLargeBannerOnDisable;

    private void OnEnable()
    {
        _ = TreatAds();
    }
    private void Start()
    {
        _ = TreatAds();
    }

    public async Task TreatAds()
    {
        await Task.Delay(TimeSpan.FromSeconds(delay));

        switch (treatBanner)
        {
            case EnumsJar_Salon.ToDo.Hide:
                AdsCallerSalon.Instance.HideBanner();
                break;
            case EnumsJar_Salon.ToDo.Show:
                AdsCallerSalon.Instance.ShowBanner();
                break;
        }

        switch (treatLargeBanner)
        {
            case EnumsJar_Salon.ToDo.Hide:
                AdsCallerSalon.Instance.HideLargeBanner();
                break;
            case EnumsJar_Salon.ToDo.Show:
                AdsCallerSalon.Instance.ShowLargeBanner();
                break;
        }

        if (showInterstitial) AdsCallerSalon.Instance.ShowInterstitial();
    }

    private void OnDisable()
    {
        // 1. Add this safety check to handle random destruction order on game exit
        if (AdsCallerSalon.Instance == null) return;

        if (invertBannerOnDisable)
        {
            switch (treatBanner)
            {
                case EnumsJar_Salon.ToDo.Hide:
                    AdsCallerSalon.Instance.StartBannerCycle();
                    AdsCallerSalon.Instance.ShowBanner();
                    break;
                case EnumsJar_Salon.ToDo.Show:
                    AdsCallerSalon.Instance.HideBanner();
                    break;
            }
        }

        if (invertLargeBannerOnDisable)
        {
            switch (treatLargeBanner)
            {
                case EnumsJar_Salon.ToDo.Hide:
                    AdsCallerSalon.Instance.ShowLargeBanner();
                    break;
                case EnumsJar_Salon.ToDo.Show:
                    AdsCallerSalon.Instance.HideLargeBanner();
                    break;
            }
        }
    }
}